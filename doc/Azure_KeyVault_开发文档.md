# Azure Key Vault .NET 开发文档

> 版本：v1.0 | 更新日期：2026-04-22

---

## 目录

1. [Key Vault 概述](#1-key-vault-概述)
2. [前置准备与权限控制](#2-前置准备与权限控制)
3. [环境变量配置（最佳实践）](#3-环境变量配置最佳实践)
4. [NuGet 包安装](#4-nuget-包安装)
5. [Secret 操作（增删改查）](#5-secret-操作增删改查)
6. [Key 操作（加密密钥管理）](#6-key-操作加密密钥管理)
7. [Certificate 操作（证书管理）](#7-certificate-操作证书管理)
8. [推荐架构与最佳实践](#8-推荐架构与最佳实践)
9. [常见问题与排查](#9-常见问题与排查)

---

## 1. Key Vault 概述

### 1.1 什么是 Azure Key Vault

Azure Key Vault 是一项云服务，用于安全地存储和访问机密信息（Secrets）、加密密钥（Keys）和证书（Certificates）。它提供了：

| 能力 | 说明 | 典型场景 |
|------|------|----------|
| **Secret 管理** | 存储连接字符串、API Key、密码等敏感字符串 | 数据库连接串、第三方 API Key |
| **Key 管理** | 创建和控制加密密钥（RSA / EC） | 数据加密/解密、数字签名 |
| **Certificate 管理** | 管理 SSL/TLS 证书和 S/MIME 证书 | HTTPS 证书自动续期 |
| **HSM 支持** | 使用 FIPS 140-2 Level 2 验证的硬件安全模块 | 高合规要求场景 |

### 1.2 核心优势

- **集中管理**：所有敏感信息统一存储，不再散落在配置文件或代码中
- **访问控制**：基于 Azure AD（Entra ID）的精细权限控制
- **审计日志**：所有访问自动记录到 Azure Monitor，支持合规审计
- **自动轮换**：配合 Azure 自动化可实现密钥/证书的自动轮换
- **零信任架构**：应用通过 Managed Identity 获取访问权限，无需硬编码凭证

### 1.3 Key Vault 对象类型对比

```
Key Vault
├── Secrets    → 字符串型敏感数据（连接串、密码、Token）
├── Keys       → 非对称加密密钥（RSA / EC），支持加密/解密/签名/验签
└── Certificates → X.509 证书（含私钥），支持自动续期策略
```

---

## 2. 前置准备与权限控制

### 2.1 创建 Key Vault

**方式一：Azure Portal**

1. 搜索 "Key Vaults" → 点击 "Create"
2. 填写基本信息（名称、订阅、资源组、区域）
3. 选择定价层（Standard / Premium）
4. 配置访问权限模型

**方式二：Azure CLI**

```bash
# 创建资源组（如已有可跳过）
az group create --name myResourceGroup --location eastasia

# 创建 Key Vault（启用 soft-delete 和 purge-protection）
az keyvault create \
  --name my-key-vault-001 \
  --resource-group myResourceGroup \
  --location eastasia \
  --enable-soft-delete true \
  --enable-purge-protection true \
  --default-action Deny \
  --bypass AzureServices
```

> ⚠️ **强烈建议**启用 `soft-delete`（软删除）和 `purge-protection`（清除保护），防止误删导致数据永久丢失。

**方式三：ARM / Bicep 模板（推荐用于团队协作）**

```bicep
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'my-key-vault-001'
  location: resourceGroup().location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      name: 'standard'
      family: 'A'
    }
    enableSoftDelete: true
    enablePurgeProtection: true
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      ipRules: [
        {
          value: '你的IP地址/32'  // 限制仅允许特定 IP 访问
        }
      ]
    }
    accessPolicies: []  // 推荐使用 RBAC，此处留空
  }
}
```

### 2.2 权限控制模型（关键）

Azure Key Vault 提供两种权限模型，**务必选择 RBAC 模型**：

#### ❌ 旧模型：Access Policy（访问策略）

```
Key Vault → Access Policies → 每个对象单独授权
缺点：配置繁琐、不支持条件、最多 16 条策略、与 Azure AD 不一致
```

#### ✅ 推荐模型：RBAC（基于角色的访问控制）

```
Azure → RBAC → Key Vault 资源级别授权
优点：统一权限模型、支持条件访问、精细权限控制、与 Azure AD 完全一致
```

**如何切换到 RBAC 模型**（创建时选择，或后续修改）：

```bash
# 将 Key Vault 的权限模型设为 RBAC
az keyvault update --name my-key-vault-001 --enable-rbac-authorization true
```

### 2.3 RBAC 内置角色说明

| 角色 | 权限范围 | 适用对象 |
|------|----------|----------|
| `Key Vault Secrets Officer` | 读取、写入、删除、备份 Secrets | Secret 管理员 |
| `Key Vault Secrets User` | 读取 Secrets | 开发者（读取配置） |
| `Key Vault Crypto Officer` | 管理加密密钥 | 密钥管理员 |
| `Key Vault Crypto User` | 使用密钥进行加密/解密/签名 | 应用服务账户 |
| `Key Vault Certificate Officer` | 管理证书 | 证书管理员 |
| `Key Vault Certificates Officer` | 管理证书策略 | DevOps |
| `Key Vault Contributor` | 管理 Key Vault 本身（不含数据） | 基础设施管理员 |
| `Key Vault Reader` | 只读查看 Key Vault 元数据 | 审计人员 |

### 2.4 为应用授予访问权限

**推荐方式：使用 Managed Identity（托管标识）**

```bash
# 1. 为 Web App 启用系统分配的托管标识
az webapp identity assign --resource-group myResourceGroup --name my-web-app

# 2. 获取应用的 Principal ID
APP_IDENTITY=$(az webapp identity show \
  --resource-group myResourceGroup \
  --name my-web-app \
  --query principalId -o tsv)

# 3. 授予 Secret 读取权限
az role assignment create \
  --assignee $APP_IDENTITY \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001
```

**本地开发方式：使用开发者个人 Azure AD 账户**

```bash
# 授予当前用户 Secret 读写权限（仅限开发环境）
CURRENT_USER=$(az ad signed-in-user show --query id -o tsv)

az role assignment create \
  --assignee $CURRENT_USER \
  --role "Key Vault Secrets Officer" \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001
```

### 2.5 网络访问控制（安全加固）

```bash
# 允许特定虚拟网络和 IP 访问
az keyvault network-rule add \
  --name my-key-vault-001 \
  --resource-group myResourceGroup \
  --subnet /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.Network/virtualNetworks/<vnet>/subnets/<subnet>

# 允许受信任的 Microsoft 服务（如 App Service、Azure Functions）绕过网络规则
az keyvault update \
  --name my-key-vault-001 \
  --resource-group myResourceGroup \
  --bypass "AzureServices"
```

### 2.6 最小权限原则（RBAC 精细化配置）

对于生产环境的应用，应遵循最小权限原则，仅授予必要的操作权限：

```
生产应用 → Key Vault Secrets User（只读 Secret）
DevOps 流水线 → Key Vault Secrets Officer（管理 Secret）
运维人员 → Key Vault Reader（审计查看）
```

---

## 3. 环境变量配置（最佳实践）

### 3.1 代码中硬编码 Vault URL 是安全的

Key Vault 的 Vault URL（如 `https://my-key-vault-001.vault.azure.net/`）本身不是敏感信息，公开不会造成安全风险。真正的安全保障来自 Azure AD 认证和 RBAC 授权。

因此，以下配置方式是**安全且推荐**的：

```
方式一（推荐）: appsettings.json 中配置 Vault URL
方式二: 环境变量配置 Vault URL
方式三: 代码中直接使用 Vault URL 常量
```

### 3.2 appsettings.json 配置示例

```json
{
  "AzureKeyVault": {
    "VaultUrl": "https://my-key-vault-001.vault.azure.net/",
    "TenantId": "your-tenant-id"
  },
  "ConnectionStrings": {
    "DefaultConnection": ""  // 不在这里放真实连接串，从 Key Vault 读取
  }
}
```

> **安全说明**：Vault URL 是公开可枚举的资源定位符，类似网站域名。真正的安全依赖于 Azure AD 认证和 RBAC 权限控制，而不是隐藏 Vault URL。因此将 Vault URL 放在 appsettings.json 中是安全的。

---

## 4. NuGet 包安装

根据 .NET 版本选择对应的包：

### .NET 8+ / ASP.NET Core

```bash
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
dotnet add package Azure.Identity
```

- `Azure.Extensions.AspNetCore.Configuration.Secrets`：将 Key Vault 集成为 ASP.NET Core 配置源
- `Azure.Identity`：提供 `DefaultAzureCredential`，自动处理认证

### 通用 .NET（Framework / Core）

```bash
dotnet add package Azure.Security.KeyVault.Secrets
dotnet add package Azure.Security.KeyVault.Keys
dotnet add package Azure.Security.KeyVault.Certificates
dotnet add package Azure.Identity
```

| 包名 | 用途 |
|------|------|
| `Azure.Security.KeyVault.Secrets` | Secret 增删改查 |
| `Azure.Security.KeyVault.Keys` | 密钥管理（加密/解密/签名） |
| `Azure.Security.KeyVault.Certificates` | 证书管理 |
| `Azure.Identity` | 认证（DefaultAzureCredential） |

---

## 5. Secret 操作（增删改查）

Secret 是 Key Vault 中最常用的对象类型，用于存储字符串型敏感数据。

### 5.1 ASP.NET Core 集成（推荐方式）

这是最简单、最推荐的方式 —— **将 Key Vault 作为配置源，直接注入使用**：

```csharp
// Program.cs (.NET 8+)
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// 添加 Key Vault 配置源（放在 AddJsonFile 之后，优先级更高）
var keyVaultUrl = new Uri(builder.Configuration["AzureKeyVault:VaultUrl"]!);
var tenantId = builder.Configuration["AzureKeyVault:TenantId"];

builder.Configuration.AddAzureKeyVault(
    keyVaultUrl,
    new DefaultAzureCredential(
        new DefaultAzureCredentialOptions
        {
            TenantId = tenantId,
            // 本地开发时指定 InteractiveBrowserTenantId 可以触发浏览器登录
            // ManagedIdentityClientId = "your-managed-identity-client-id" // 指定托管标识
        }
    )
);

// 直接通过 IConfiguration 读取，与普通配置完全一致
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var apiKey = builder.Configuration["ThirdParty:ApiKey"];

builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.Run();
```

```csharp
// Controller 中使用 —— 与普通依赖注入完全一致
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    private readonly IConfiguration _config;

    public DataController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var connStr = _config.GetConnectionString("DefaultConnection");
        var apiKey = _config["ThirdParty:ApiKey"];
        return Ok(new { ConnectionString = connStr?.Substring(0, 20) + "...", HasApiKey = !string.IsNullOrEmpty(apiKey) });
    }
}
```

> **为什么推荐这种方式？** 应用代码不需要引用 `Azure.Security.KeyVault.Secrets`，不需要调用任何 Key Vault SDK。Secret 就像普通配置项一样使用，实现了真正的**配置无关性**。

### 5.2 直接操作 Secret（SDK 方式）

适用于需要程序化管理 Secret 的场景（如运维工具、部署脚本）：

```csharp
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

public class KeyVaultSecretService
{
    private readonly SecretClient _secretClient;

    public KeyVaultSecretService(string vaultUrl, string? tenantId = null)
    {
        var credential = new DefaultAzureCredential(
            tenantId != null
                ? new DefaultAzureCredentialOptions { TenantId = tenantId }
                : new DefaultAzureCredentialOptions());

        _secretClient = new SecretClient(
            new Uri(vaultUrl),
            credential);
    }

    // ========== 添加/更新 Secret ==========

    /// <summary>
    /// 设置（添加或更新）Secret
    /// 如果 Secret 已存在则更新其值，不存在则创建
    /// </summary>
    public async Task<KeyVaultSecret> SetSecretAsync(string name, string value)
    {
        try
        {
            var secret = await _secretClient.SetSecretAsync(new KeyVaultSecret(name, value));
            Console.WriteLine($"✅ Secret '{name}' 已设置，版本: {secret.Value.Properties.Version}");
            return secret.Value;
        }
        catch (RequestFailedException ex)
        {
            Console.WriteLine($"❌ 设置 Secret 失败: {ex.Status} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 设置带过期时间的 Secret
    /// </summary>
    public async Task<KeyVaultSecret> SetSecretWithExpiryAsync(
        string name,
        string value,
        TimeSpan expiresAfter)
    {
        var secret = new KeyVaultSecret(name, value)
        {
            Properties =
            {
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiresAfter)
            }
        };

        var result = await _secretClient.SetSecretAsync(secret);
        return result.Value;
    }

    /// <summary>
    /// 设置带标签（Tag）的 Secret，方便分类管理
    /// </summary>
    public async Task<KeyVaultSecret> SetSecretWithTagsAsync(
        string name,
        string value,
        Dictionary<string, string> tags)
    {
        var secret = new KeyVaultSecret(name, value);

        foreach (var tag in tags)
        {
            secret.Properties.Tags[tag.Key] = tag.Value;
        }

        var result = await _secretClient.SetSecretAsync(secret);
        return result.Value;
    }

    // ========== 读取 Secret ==========

    /// <summary>
    /// 获取 Secret 最新版本
    /// </summary>
    public async Task<string?> GetSecretAsync(string name)
    {
        try
        {
            var response = await _secretClient.GetSecretAsync(name);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            Console.WriteLine($"⚠️ Secret '{name}' 不存在");
            return null;
        }
    }

    /// <summary>
    /// 获取指定版本的 Secret
    /// </summary>
    public async Task<string?> GetSecretVersionAsync(string name, string version)
    {
        var response = await _secretClient.GetSecretAsync(name, version);
        return response.Value.Value;
    }

    /// <summary>
    /// 获取 Secret 属性（不包含实际值，适合列表展示）
    /// </summary>
    public async Task<IReadOnlyList<SecretProperties>> ListSecretsAsync()
    {
        var secrets = new List<SecretProperties>();

        await foreach (var secretProperties in _secretClient.GetPropertiesOfSecretsAsync())
        {
            secrets.Add(secretProperties);
        }

        return secrets.AsReadOnly();
    }

    /// <summary>
    /// 获取 Secret 的所有版本
    /// </summary>
    public async Task<IReadOnlyList<SecretProperties>> GetSecretVersionsAsync(string name)
    {
        var versions = new List<SecretProperties>();

        await foreach (var version in _secretClient.GetPropertiesOfSecretVersionsAsync(name))
        {
            versions.Add(version);
        }

        return versions.AsReadOnly();
    }

    // ========== 删除 Secret ==========

    /// <summary>
    /// 软删除 Secret（启用 soft-delete 时，可在 90 天内恢复）
    /// </summary>
    public async Task DeleteSecretAsync(string name)
    {
        try
        {
            var operation = await _secretClient.StartDeleteSecretAsync(name);
            await operation.WaitForCompletionAsync();
            Console.WriteLine($"✅ Secret '{name}' 已软删除（可在 90 天内恢复）");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            Console.WriteLine($"⚠️ Secret '{name}' 不存在，无需删除");
        }
    }

    /// <summary>
    /// 永久删除 Secret（不可恢复！需要先执行软删除，且已超出保留期）
    /// </summary>
    public async Task PurgeSecretAsync(string name)
    {
        try
        {
            await _secretClient.PurgeDeletedSecretAsync(name);
            Console.WriteLine($"✅ Secret '{name}' 已永久删除");
        }
        catch (RequestFailedException ex)
        {
            Console.WriteLine($"❌ 永久删除失败: {ex.Status} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 恢复已软删除的 Secret
    /// </summary>
    public async Task RecoverSecretAsync(string name)
    {
        var operation = await _secretClient.StartRecoverDeletedSecretAsync(name);
        await operation.WaitForCompletionAsync();
        Console.WriteLine($"✅ Secret '{name}' 已恢复");
    }

    // ========== 备份与恢复 ==========

    /// <summary>
    /// 备份 Secret（返回加密的备份字节）
    /// </summary>
    public async Task<byte[]> BackupSecretAsync(string name)
    {
        var response = await _secretClient.BackupSecretAsync(name);
        return response.Value;
    }

    /// <summary>
    /// 从备份恢复 Secret
    /// </summary>
    public async Task<KeyVaultSecret> RestoreSecretAsync(byte[] backup)
    {
        var response = await _secretClient.RestoreSecretBackupAsync(backup);
        return response.Value;
    }
}
```

### 5.3 使用示例

```csharp
// 使用示例
var service = new KeyVaultSecretService(
    vaultUrl: "https://my-key-vault-001.vault.azure.net/",
    tenantId: "your-tenant-id");

// 添加 Secret
await service.SetSecretWithTagsAsync(
    "Database-ConnectionString",
    "Server=tcp:myserver.database.windows.net,1433;Database=mydb;User ID=admin;Password=xxx;",
    new Dictionary<string, string>
    {
        { "Environment", "Production" },
        { "Application", "WebApp" },
        { "Owner", "Team-A" }
    });

// 读取 Secret
var connStr = await service.GetSecretAsync("Database-ConnectionString");
Console.WriteLine(connStr);

// 列出所有 Secret
var secrets = await service.ListSecretsAsync();
foreach (var secret in secrets)
{
    Console.WriteLine($"- {secret.Name} | Created: {secret.CreatedOn} | Tags: {string.Join(", ", secret.Tags.Select(t => $"{t.Key}={t.Value}"))}");
}

// 软删除（安全删除）
await service.DeleteSecretAsync("Old-Api-Key");

// 彻底删除（不可恢复，谨慎使用！）
// await service.PurgeSecretAsync("Old-Api-Key");
```

---

## 6. Key 操作（加密密钥管理）

Key Vault Key 提供硬件级别的密钥管理，支持加密/解密和数字签名。

### 6.1 密钥服务封装

```csharp
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

public class KeyVaultKeyService
{
    private readonly KeyClient _keyClient;

    public KeyVaultKeyService(string vaultUrl, string? tenantId = null)
    {
        var credential = new DefaultAzureCredential(
            tenantId != null
                ? new DefaultAzureCredentialOptions { TenantId = tenantId }
                : new DefaultAzureCredentialOptions());

        _keyClient = new KeyClient(new Uri(vaultUrl), credential);
    }

    /// <summary>
    /// 创建 RSA 密钥
    /// </summary>
    public async Task<KeyVaultKey> CreateRsaKeyAsync(string name, int keySize = 2048)
    {
        var key = await _keyClient.CreateKeyAsync(
            new CreateRsaKeyOptions(name)
            {
                KeySize = keySize,
                ExpiresOn = DateTimeOffset.UtcNow.AddYears(2)
            });
        return key.Value;
    }

    /// <summary>
    /// 创建 EC（椭圆曲线）密钥
    /// </summary>
    public async Task<KeyVaultKey> CreateEcKeyAsync(string name)
    {
        var key = await _keyClient.CreateKeyAsync(
            new CreateEcKeyOptions(name)
            {
                CurveName = KeyCurveName.P256
            });
        return key.Value;
    }

    /// <summary>
    /// 获取密钥的加密/解密客户端
    /// </summary>
    public CryptographyClient GetCryptographyClient(string keyName, string? keyVersion = null)
    {
        var key = _keyClient.GetKeyAsync(keyName, keyVersion).GetAwaiter().GetResult();
        return new CryptographyClient(key.Value.Id, new DefaultAzureCredential());
    }

    /// <summary>
    /// 加密数据
    /// </summary>
    public async Task<byte[]> EncryptAsync(string keyName, byte[] plaintext)
    {
        var cryptoClient = GetCryptographyClient(keyName);
        var result = await cryptoClient.EncryptAsync(
            EncryptionAlgorithm.RsaOaep,
            plaintext);
        return result.Ciphertext;
    }

    /// <summary>
    /// 解密数据
    /// </summary>
    public async Task<byte[]> DecryptAsync(string keyName, byte[] ciphertext)
    {
        var cryptoClient = GetCryptographyClient(keyName);
        var result = await cryptoClient.DecryptAsync(
            EncryptionAlgorithm.RsaOaep,
            ciphertext);
        return result.Plaintext;
    }

    /// <summary>
    /// 列出所有密钥
    /// </summary>
    public async Task<IReadOnlyList<KeyProperties>> ListKeysAsync()
    {
        var keys = new List<KeyProperties>();
        await foreach (var keyProperties in _keyClient.GetPropertiesOfKeysAsync())
        {
            keys.Add(keyProperties);
        }
        return keys.AsReadOnly();
    }

    /// <summary>
    /// 软删除密钥
    /// </summary>
    public async Task DeleteKeyAsync(string name)
    {
        var operation = await _keyClient.StartDeleteKeyAsync(name);
        await operation.WaitForCompletionAsync();
    }
}
```

### 6.2 使用示例

```csharp
var keyService = new KeyVaultKeyService(
    vaultUrl: "https://my-key-vault-001.vault.azure.net/",
    tenantId: "your-tenant-id");

// 创建 RSA 密钥
var key = await keyService.CreateRsaKeyAsync("data-encryption-key", keySize: 4096);
Console.WriteLine($"密钥创建成功: {key.Name}, KeyType: {key.KeyType}");

// 加密示例
var originalText = "这是需要加密的敏感数据";
var plainBytes = System.Text.Encoding.UTF8.GetBytes(originalText);
var encryptedBytes = await keyService.EncryptAsync("data-encryption-key", plainBytes);

// 解密示例
var decryptedBytes = await keyService.DecryptAsync("data-encryption-key", encryptedBytes);
var decryptedText = System.Text.Encoding.UTF8.GetString(decryptedBytes);
Console.WriteLine($"解密结果: {decryptedText}");
```

---

## 7. Certificate 操作（证书管理）

### 7.1 证书服务封装

```csharp
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;

public class KeyVaultCertificateService
{
    private readonly CertificateClient _certClient;

    public KeyVaultCertificateService(string vaultUrl, string? tenantId = null)
    {
        var credential = new DefaultAzureCredential(
            tenantId != null
                ? new DefaultAzureCredentialOptions { TenantId = tenantId }
                : new DefaultAzureCredentialOptions());

        _certClient = new CertificateClient(new Uri(vaultUrl), credential);
    }

    /// <summary>
    /// 创建自签名证书
    /// </summary>
    public async Task<KeyVaultCertificateWithPolicy> CreateSelfSignedCertificateAsync(
        string name,
        string subjectName,
        int validityMonths = 12)
    {
        var policy = new CertificatePolicy(subjectName)
        {
            IssuerName = "Self",
            ValidityInMonths = validityMonths,
            KeySize = 2048,
            ReuseKeyOnRenewal = true,  // 续期时复用密钥
            ContentType = CertificateContentType.Pkcs12
        };

        var operation = await _certClient.StartCreateCertificateAsync(name, policy);
        var certificate = await operation.WaitForCompletionAsync();
        return certificate.Value;
    }

    /// <summary>
    /// 导入已有的 PFX 证书
    /// </summary>
    public async Task<KeyVaultCertificateWithPolicy> ImportCertificateAsync(
        string name,
        byte[] pfxData,
        string password)
    {
        var certificate = await _certClient.ImportCertificateAsync(
            new ImportCertificateOptions(name, pfxData)
            {
                Password = password
            });
        return certificate.Value;
    }

    /// <summary>
    /// 获取证书（含公钥）
    /// </summary>
    public async Task<KeyVaultCertificate?> GetCertificateAsync(string name)
    {
        try
        {
            var response = await _certClient.GetCertificateAsync(name);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// 列出所有证书
    /// </summary>
    public async Task<IReadOnlyList<CertificateProperties>> ListCertificatesAsync()
    {
        var certs = new List<CertificateProperties>();
        await foreach (var certProperties in _certClient.GetPropertiesOfCertificatesAsync())
        {
            certs.Add(certProperties);
        }
        return certs.AsReadOnly();
    }

    /// <summary>
    /// 获取证书的 CRL 取消列表
    /// </summary>
    public async Task<byte[]> GetCertificateCrlAsync(string name)
    {
        var response = await _certClient.GetCertificatePolicyAsync(name);
        return new byte[0]; // 实际 CRL 需配合 CA 机构
    }

    /// <summary>
    /// 软删除证书
    /// </summary>
    public async Task DeleteCertificateAsync(string name)
    {
        var operation = await _certClient.StartDeleteCertificateAsync(name);
        await operation.WaitForCompletionAsync();
    }
}
```

### 7.2 使用示例

```csharp
var certService = new KeyVaultCertificateService(
    vaultUrl: "https://my-key-vault-001.vault.azure.net/",
    tenantId: "your-tenant-id");

// 创建自签名证书
var cert = await certService.CreateSelfSignedCertificateAsync(
    name: "myapp-cert",
    subjectName: "CN=myapp.example.com",
    validityMonths: 24);

Console.WriteLine($"证书创建成功: {cert.Name}, 过期时间: {cert.Properties.ExpiresOn}");

// 导入已有 PFX 证书
// var pfxBytes = File.ReadAllBytes("path/to/cert.pfx");
// await certService.ImportCertificateAsync("imported-cert", pfxBytes, "pfx-password");

// 列出所有证书
var certs = await certService.ListCertificatesAsync();
foreach (var c in certs)
{
    Console.WriteLine($"- {c.Name} | Expires: {c.ExpiresOn} | X509: {c.X509Thumbprint}");
}
```

---

## 8. 推荐架构与最佳实践

### 8.1 实践对比

| 实践方式 | 安全性 | 可维护性 | 推荐度 | 适用场景 |
|----------|--------|----------|--------|----------|
| **appsettings.json + Key Vault 配置源** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ✅ 最推荐 | ASP.NET Core 应用 |
| **IConfiguration 直接注入** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ✅ 最推荐 | 通用 .NET Core |
| **DefaultAzureCredential + Managed Identity** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ✅ 推荐 | 生产环境 |
| **Service Principal + Client Secret** | ⭐⭐⭐ | ⭐⭐⭐ | ⚠️ 不推荐 | 仅限 CI/CD |
| **直接操作 SDK（非配置源）** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⚠️ 仅管理场景 | 运维工具 |
| **硬编码 AK/SK** | ❌ 极不安全 | ❌ | ❌ 禁止 | — |

### 8.2 推荐架构图

```
┌─────────────────────────────────────────────────────┐
│                   应用层                              │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
│  │ Web App  │  │ API App  │  │ Azure Fn │           │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘           │
│       │              │              │                  │
│       └──────────────┼──────────────┘                  │
│                      │                                 │
│           ┌──────────▼──────────┐                     │
│           │  DefaultAzureCred.  │  ← Managed Identity │
│           └──────────┬──────────┘                     │
│                      │                                 │
├──────────────────────┼────────────────────────────────┤
│               Azure AD (Entra ID)                     │
│           ┌──────────▼──────────┐                     │
│           │     RBAC 授权        │  ← 精细权限控制      │
│           └──────────┬──────────┘                     │
│                      │                                 │
│           ┌──────────▼──────────┐                     │
│           │    Azure Key Vault  │                     │
│           │  ┌───────┐┌───────┐│                     │
│           │  │Secrets││ Keys  ││                     │
│           │  │       ││       ││                     │
│           │  │  DB   ││ RSA   ││                     │
│           │  │  API   ││ EC    ││                     │
│           │  │  Key   ││       ││                     │
│           │  └───────┘└───────┘│                     │
│           │  ┌───────────────┐ │                     │
│           │  │ Certificates  │ │                     │
│           │  │ (SSL/TLS)     │ │                     │
│           │  └───────────────┘ │                     │
│           └────────────────────┘                     │
└─────────────────────────────────────────────────────┘
```

### 8.3 最佳实践清单

#### ✅ 应该做的

1. **始终使用 Managed Identity** —— 应用通过系统分配或用户分配的托管标识访问 Key Vault，零密钥管理
2. **使用 RBAC 权限模型** —— 放弃 Access Policy，统一使用 Azure RBAC
3. **启用 soft-delete 和 purge-protection** —— 防止误删导致数据永久丢失
4. **使用 DefaultAzureCredential** —— 自动适配多种认证环境（本地开发、Azure 托管、CI/CD）
5. **将 Key Vault 作为配置源** —— 使用 `AddAzureKeyVault()` 融入 IConfiguration，保持代码简洁
6. **设置 Secret 过期时间** —— 为每个 Secret 设置合理的过期时间，配合自动轮换策略
7. **使用 Tag 标记管理** —— 为 Secret 添加 Environment、Application、Owner 等 Tag，方便管理
8. **限制网络访问** —— 配置 Firewall 规则，仅允许必要的 IP 和虚拟网络访问
9. **启用审计日志** —— 将 Key Vault 日志发送到 Log Analytics，支持合规审计和异常检测
10. **配置自动轮换** —— 结合 Azure Event Grid + Functions 实现 Secret 自动轮换

#### ❌ 不应该做的

1. **禁止硬编码 Vault URL 以外的任何凭证** —— 不在代码中放 AK/SK、Client Secret
2. **禁止为应用分配过大的权限** —— 生产应用只需要 `Key Vault Secrets User`（只读）
3. **禁止关闭 soft-delete** —— 否则删除操作不可逆
4. **禁止将 Secret 直接输出到日志** —— 避免敏感信息泄露
5. **禁止所有网络可访问** —— 至少配置 IP 白名单或虚拟网络限制
6. **禁止共享 Service Principal** —— 每个应用使用独立的 Managed Identity
7. **禁止跳过 RBAC 直接使用 Access Policy** —— 新建 Key Vault 必须使用 RBAC 模型

### 8.4 Secret 命名规范

```
推荐格式: {Application}-{Environment}-{DataType}-{Description}

示例:
- WebApp-Prod-ConnectionString-Database     → 数据库连接串
- ApiApp-Prod-Secret-StripeApiKey            → Stripe API Key
- MobileApp-Staging-Secret-PushNotification  → 推送通知密钥
- Common-Prod-Cert-AppService                → 应用服务证书
```

> 使用 `-` 分隔，不使用 `.` 或 `_`，因为某些平台对点号有特殊处理。

### 8.5 ASP.NET Core 完整集成示例

```csharp
// Program.cs
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// 1. 读取基础配置
var vaultUrl = builder.Configuration["AzureKeyVault:VaultUrl"];
var tenantId = builder.Configuration["AzureKeyVault:TenantId"];

// 2. 添加 Key Vault 作为配置源（优先级高于 appsettings.json）
if (!string.IsNullOrEmpty(vaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(vaultUrl),
        new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { TenantId = tenantId }));

    Console.WriteLine("✅ Key Vault 配置源已加载");
}

// 3. 正常使用配置 —— Key Vault 中的值会自动覆盖本地配置
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection("DatabaseSettings"));

// 4. 可选：强类型配置类
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

var app = builder.Build();
app.Run();

// 配置类
public class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeout { get; set; } = 30;
}

public class EmailSettings
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;  // 从 Key Vault 读取
}
```

### 8.6 .NET Framework (ASP.NET MVC) 集成

对于 .NET Framework 4.6.1+ 项目：

```csharp
// NuGet: Azure.Extensions.AspNetCore.Configuration.Secrets（需要 .NET Standard 2.0）
// 或使用 Azure.Security.KeyVault.Secrets + Azure.Identity

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

public static class KeyVaultConfig
{
    private static readonly SecretClient Client = new(
        new Uri("https://my-key-vault-001.vault.azure.net/"),
        new DefaultAzureCredential());

    public static string Get(string secretName)
    {
        try
        {
            var secret = Client.GetSecretAsync(secretName).GetAwaiter().GetResult();
            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            // 降级：从 Web.config 读取
            return System.Configuration.ConfigurationManager.AppSettings[secretName]
                   ?? throw new InvalidOperationException($"无法获取配置 '{secretName}': {ex.Message}");
        }
    }
}

// 使用
var connectionString = KeyVaultConfig.Get("Database-ConnectionString");
```

> ⚠️ .NET Framework 的 `DefaultAzureCredential` 仅支持 `VisualStudioCredential`、`AzureCliCredential`、`EnvironmentCredential` 等，不支持 `ManagedIdentityCredential`（除非运行在 Azure App Service 中）。

---

## 9. 常见问题与排查

### 9.1 常见错误码

| 错误码 | 含义 | 解决方案 |
|--------|------|----------|
| `401 Unauthorized` | 认证失败 | 检查 Managed Identity 是否启用，或 Azure CLI 是否已登录 |
| `403 Forbidden` | 权限不足 | 检查 RBAC 角色分配，确认角色范围是否正确 |
| `404 Not Found` | Secret/Key 不存在 | 检查名称拼写，或查看是否在已删除列表中 |
| `429 Too Many Requests` | 请求频率超限 | Key Vault 限制 1000 次/10秒，实现重试策略 |
| `Forbidden / Network` | 网络访问被拒绝 | 检查 Key Vault 防火墙规则和网络 ACL |

### 9.2 本地开发认证流程

`DefaultAzureCredential` 按以下顺序尝试认证：

```
1. EnvironmentCredential     → 环境变量 AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET
2. WorkloadIdentityCredential → Kubernetes Workload Identity
3. ManagedIdentityCredential  → Azure 托管标识（App Service、VM 等）
4. VisualStudioCredential     → Visual Studio Azure 登录
5. AzureCliCredential         → Azure CLI 登录（az login）
6. AzurePowerShellCredential  → Azure PowerShell 登录（Connect-AzAccount）
7. InteractiveBrowserCredential → 浏览器交互式登录（最后兜底）
```

> **本地开发最简方式**：执行 `az login` 登录后，`DefaultAzureCredential` 会自动使用 Azure CLI 凭证。

### 9.3 排查清单

```bash
# 1. 确认 Azure CLI 已登录
az login
az account show

# 2. 确认当前用户身份
az ad signed-in-user show --query "{id:id, displayName:displayName}"

# 3. 检查 RBAC 角色分配
az role assignment list \
  --assignee $(az ad signed-in-user show --query id -o tsv) \
  --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/<vault-name> \
  --output table

# 4. 检查 Key Vault 状态
az keyvault show --name <vault-name> --resource-group <rg>

# 5. 测试 Secret 访问
az keyvault secret show --vault-name <vault-name> --name <secret-name>

# 6. 检查网络规则
az keyvault network-rule list --name <vault-name> --resource-group <rg>
```

---

## 附录

### A. Key Vault 定价参考

| 层级 | Secret 操作 | Key 操作 | 证书操作 | HSM |
|------|------------|----------|----------|-----|
| Standard | 10,000 次/30天免费 | 10,000 次/30天免费 | 证书操作有额外费用 | ❌ 不支持 |
| Premium | 同上 | 同上 | 同上 | ✅ 支持 |

> 超出免费额度后，按每次操作计费（约 $0.03/10,000 次操作）。

### B. 相关文档链接

- [Azure Key Vault 官方文档](https://learn.microsoft.com/azure/key-vault/)
- [.NET SDK 参考文档](https://learn.microsoft.com/dotnet/api/azure.security.keyvault.secrets)
- [RBAC 权限模型指南](https://learn.microsoft.com/azure/key-vault/general/rbac-guide)
- [DefaultAzureCredential 说明](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
- [ASP.NET Core Key Vault 配置提供程序](https://learn.microsoft.com/aspnet/core/security/key-vault-configuration)

---

*本文档基于 Azure SDK 4.x 编写，适用于 .NET 6+ / .NET Framework 4.6.1+。*
