# ASP.NET Framework MVC — Azure KeyVault 操作手册

> 版本：v1.0 | 日期：2026-05-28 | 适用框架：ASP.NET Framework 4.7.2+

---

## 目录

1. [概述与架构](#1-概述与架构)
2. [环境准备与 NuGet 包](#2-环境准备与-nuget-包)
3. [认证方式说明](#3-认证方式说明)
4. [KeyVaultService — 统一服务类](#4-keyvaultservice--统一服务类)
   - 4.1 [服务类定义与初始化](#41-服务类定义与初始化)
   - 4.2 [Secret 操作（CRUD）](#42-secret-操作crud)
   - 4.3 [Key 操作（CRUD）](#43-key-操作crud)
   - 4.4 [Certificate 操作（CRUD）](#44-certificate-操作crud)
   - 4.5 [备份与恢复](#45-备份与恢复)
5. [Controller 层调用示例](#5-controller-层调用示例)
6. [Web.config 配置](#6-webconfig-配置)
7. [RBAC 权限配置速查](#7-rbac-权限配置速查)
8. [最佳实践与注意事项](#8-最佳实践与注意事项)
9. [常见错误排查](#9-常见错误排查)

---

## 1. 概述与架构

### 1.1 Key Vault 对象类型

```
Key Vault
├── Secrets（机密）──── 存储字符串型敏感数据
│   "Database-ConnectionString"  →  "Server=tcp:mydb..."
│   "ThirdParty-ApiKey"          →  "sk-abc123xyz..."
│   特点：值始终加密存储，支持版本管理，支持自动过期
│
├── Keys（密钥）────── 存储加密密钥（由 Key Vault 硬件管理）
│   "data-encryption-key"  →  RSA-4096
│   "signing-key"          →  EC-P256
│   特点：私钥永不离开 Key Vault，支持云端加密/解密/签名/验签
│
└── Certificates（证书）── 存储 X.509 证书（含私钥）
    "myapp-ssl-cert"  →  *.example.com, 有效期至 2027-04-22
    特点：支持自动续期、SSL/TLS、S/MIME
```

### 1.2 安全信任链

```
应用程序 ──① 身份认证──▶ Azure AD (Entra ID)
                               │
                         ② RBAC 授权
                               │
                         ③ 网络隔离
                               │
应用程序 ◀──④ 返回数据── Azure Key Vault

三层防护：认证（你是谁）→ 授权（你能做什么）→ 网络（你能访问吗）
```

### 1.3 KeyVaultService 设计

| 功能模块 | SDK 包 | 主要操作 |
|---------|--------|---------|
| Secret 管理 | `Azure.Security.KeyVault.Secrets` | 增删改查、版本管理、备份恢复 |
| Key 管理 | `Azure.Security.KeyVault.Keys` | 创建/删除密钥、加密/解密/签名/验签 |
| Certificate 管理 | `Azure.Security.KeyVault.Certificates` | 导入/创建证书、获取证书链 |

---

## 2. 环境准备与 NuGet 包

### 2.1 NuGet 包安装

在 Visual Studio 的 Package Manager Console 中执行：

```powershell
# 认证
Install-Package Azure.Identity -Version 1.13.1

# Secret 操作
Install-Package Azure.Security.KeyVault.Secrets -Version 4.7.0

# Key 操作
Install-Package Azure.Security.KeyVault.Keys -Version 4.7.0

# Certificate 操作
Install-Package Azure.Security.KeyVault.Certificates -Version 4.7.0

# 交互式登录（.NET Framework 需要）
Install-Package Microsoft.Identity.Client -Version 4.66.1
```

### 2.2 项目文件结构

```
📁 项目结构:
├── Services/
│   └── KeyVaultService.cs          ← 统一服务类（本文档核心）
├── Models/
│   └── KeyVaultModels.cs          ← 数据模型
├── App_Start/
│   └── KeyVaultConfig.cs          ← 初始化配置
├── Controllers/
│   └── KeyVaultController.cs      ← 调用示例
├── Global.asax.cs                  ← 应用启动初始化
└── Web.config                      ← Vault URL 等配置
```

---

## 3. 认证方式说明

| 认证方式 | 适用场景 | 配置方法 |
|---------|---------|---------|
| **Managed Identity** | 部署到 Azure App Service / VM | Azure Portal 启用系统托管标识 |
| **EnvironmentCredential** | CI/CD（Azure DevOps） | 设置 `AZURE_TENANT_ID`、`AZURE_CLIENT_ID`、`AZURE_CLIENT_SECRET` |
| **AzureCliCredential** | 本地开发 | 执行 `az login` |
| **VisualStudioCredential** | Visual Studio IDE 调试 | VS 中登录 Azure 账户 |
| **InteractiveBrowser** | 本地开发（兜底） | 弹出浏览器登录 |

**DefaultAzureCredential** 会自动按以上顺序尝试认证，无需手动选择。

---

## 4. KeyVaultService — 统一服务类

### 4.1 服务类定义与初始化

```csharp
// ============================================
// 文件: Services/KeyVaultService.cs
// 功能: Azure Key Vault 统一操作服务
// 适用: ASP.NET Framework 4.7.2+
// ============================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;

namespace YourProject.Services
{
    /// <summary>
    /// Azure Key Vault 统一操作服务
    /// 封装 Secret / Key / Certificate 三种对象的完整 CRUD
    /// </summary>
    public class KeyVaultService
    {
        private readonly SecretClient _secretClient;
        private readonly KeyClient _keyClient;
        private readonly CertificateClient _certificateClient;
        private readonly string _vaultUrl;

        /// <summary>
        /// 初始化 Key Vault 服务
        /// </summary>
        /// <param name="vaultUrl">Key Vault URL，如 https://my-vault.vault.azure.net/</param>
        /// <param name="tenantId">Azure AD Tenant ID（可选，本地开发建议指定）</param>
        /// <param name="managedIdentityClientId">用户分配的托管标识 Client ID（可选）</param>
        public KeyVaultService(
            string vaultUrl,
            string tenantId = null,
            string managedIdentityClientId = null)
        {
            _vaultUrl = vaultUrl;

            var options = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrEmpty(tenantId))
                options.TenantId = tenantId;
            if (!string.IsNullOrEmpty(managedIdentityClientId))
                options.ManagedIdentityClientId = managedIdentityClientId;
            options.ExcludeInteractiveBrowserCredential = false;

            var credential = new DefaultAzureCredential(options);

            _secretClient = new SecretClient(
                new Uri(vaultUrl), credential);
            _keyClient = new KeyClient(
                new Uri(vaultUrl), credential);
            _certificateClient = new CertificateClient(
                new Uri(vaultUrl), credential);
        }
```

### 4.2 Secret 操作（CRUD）

#### 4.2.1 创建/更新 Secret (Set)

```csharp
        // ==========================================
        // Secret — 创建/更新
        // ==========================================

        /// <summary>
        /// 设置 Secret（存在则创建新版本，不存在则创建）
        /// </summary>
        /// <param name="name">Secret 名称</param>
        /// <param name="value">Secret 值</param>
        /// <param name="contentType">内容类型（可选）</param>
        /// <param name="expiresOn">过期时间（可选）</param>
        /// <param name="tags">标签（可选）</param>
        public async Task<KeyVaultSecret> SetSecretAsync(
            string name,
            string value,
            string contentType = null,
            DateTimeOffset? expiresOn = null,
            Dictionary<string, string> tags = null)
        {
            var secret = new KeyVaultSecret(name, value);

            if (!string.IsNullOrEmpty(contentType))
                secret.Properties.ContentType = contentType;
            if (expiresOn.HasValue)
                secret.Properties.ExpiresOn = expiresOn.Value;
            if (tags != null)
                foreach (var tag in tags)
                    secret.Properties.Tags[tag.Key] = tag.Value;

            var response = await _secretClient.SetSecretAsync(secret);
            return response.Value;
        }

        /// <summary>
        /// 批量设置 Secret
        /// </summary>
        public async Task<(int succeeded, int failed, List<string> errors)>
            SetSecretsAsync(
                Dictionary<string, string> secrets,
                Dictionary<string, string> commonTags = null)
        {
            int succeeded = 0, failed = 0;
            var errors = new List<string>();

            foreach (var kvp in secrets)
            {
                try
                {
                    await SetSecretAsync(kvp.Key, kvp.Value,
                        tags: commonTags);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{kvp.Key}: {ex.Message}");
                }
            }
            return (succeeded, failed, errors);
        }
```

#### 4.2.2 读取 Secret (Get)

```csharp
        // ==========================================
        // Secret — 读取
        // ==========================================

        /// <summary>
        /// 获取 Secret 值（最新版本）
        /// </summary>
        public async Task<string> GetSecretAsync(string name)
        {
            var response = await _secretClient.GetSecretAsync(name);
            return response.Value.Value;
        }

        /// <summary>
        /// 获取指定版本的 Secret
        /// </summary>
        public async Task<string> GetSecretVersionAsync(
            string name, string version)
        {
            var response = await _secretClient.GetSecretAsync(name, version);
            return response.Value.Value;
        }

        /// <summary>
        /// 获取 Secret 属性列表（不含值，适合管理界面）
        /// </summary>
        public async Task<List<SecretPropertiesInfo>> ListSecretsAsync()
        {
            var result = new List<SecretPropertiesInfo>();
            await foreach (var prop in _secretClient
                .GetPropertiesOfSecretsAsync())
            {
                result.Add(new SecretPropertiesInfo
                {
                    Name = prop.Name,
                    Version = prop.Version,
                    CreatedOn = prop.CreatedOn,
                    UpdatedOn = prop.UpdatedOn,
                    ExpiresOn = prop.ExpiresOn,
                    Enabled = prop.Enabled,
                    ContentType = prop.ContentType,
                    Tags = prop.Tags.ToDictionary(
                        t => t.Key, t => t.Value)
                });
            }
            return result;
        }

        /// <summary>
        /// 获取 Secret 的所有版本列表
        /// </summary>
        public async Task<List<SecretVersionInfo>>
            GetSecretVersionsAsync(string name)
        {
            var result = new List<SecretVersionInfo>();
            await foreach (var prop in _secretClient
                .GetPropertiesOfSecretVersionsAsync(name))
            {
                result.Add(new SecretVersionInfo
                {
                    Version = prop.Version,
                    CreatedOn = prop.CreatedOn,
                    UpdatedOn = prop.UpdatedOn,
                    ExpiresOn = prop.ExpiresOn,
                    Enabled = prop.Enabled
                });
            }
            return result;
        }
```

#### 4.2.3 删除 Secret (Delete)

```csharp
        // ==========================================
        // Secret — 删除
        // ==========================================

        /// <summary>
        /// 软删除 Secret（启用 soft-delete 时，90 天内可恢复）
        /// </summary>
        public async Task DeleteSecretAsync(string name)
        {
            var operation = await _secretClient
                .StartDeleteSecretAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 恢复已软删除的 Secret
        /// </summary>
        public async Task RecoverSecretAsync(string name)
        {
            var operation = await _secretClient
                .StartRecoverDeletedSecretAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 永久清除 Secret（不可恢复！需先软删除且过保留期）
        /// </summary>
        public async Task PurgeSecretAsync(string name)
        {
            await _secretClient.PurgeDeletedSecretAsync(name);
        }

        /// <summary>
        /// 获取已删除的 Secret 列表
        /// </summary>
        public async Task<List<DeletedSecretInfo>>
            ListDeletedSecretsAsync()
        {
            var result = new List<DeletedSecretInfo>();
            await foreach (var deleted in _secretClient
                .GetDeletedSecretsAsync())
            {
                result.Add(new DeletedSecretInfo
                {
                    Name = deleted.Name,
                    RecoveryId = deleted.RecoveryId?.ToString(),
                    DeletedOn = deleted.DeletedOn,
                    ScheduledPurgeDate = deleted.ScheduledPurgeDate
                });
            }
            return result;
        }
```

#### 4.2.4 检查 Secret 存在

```csharp
        // ==========================================
        // Secret — 辅助
        // ==========================================

        /// <summary>
        /// 检查 Secret 是否存在
        /// </summary>
        public async Task<bool> SecretExistsAsync(string name)
        {
            try
            {
                await _secretClient.GetSecretAsync(name);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
        }

        /// <summary>
        /// 更新 Secret 属性（启用/禁用、过期时间等，不修改值）
        /// </summary>
        public async Task UpdateSecretPropertiesAsync(
            string name, string version = null,
            bool? enabled = null,
            DateTimeOffset? expiresOn = null,
            Dictionary<string, string> tags = null)
        {
            // 先获取当前属性
            SecretProperties props;
            if (string.IsNullOrEmpty(version))
                props = (await _secretClient
                    .GetSecretAsync(name)).Value.Properties;
            else
                props = (await _secretClient
                    .GetSecretAsync(name, version)).Value.Properties;

            if (enabled.HasValue)
                props.Enabled = enabled.Value;
            if (expiresOn.HasValue)
                props.ExpiresOn = expiresOn.Value;
            if (tags != null)
            {
                props.Tags.Clear();
                foreach (var tag in tags)
                    props.Tags[tag.Key] = tag.Value;
            }

            await _secretClient.UpdateSecretPropertiesAsync(props);
        }
```

### 4.3 Key 操作（CRUD）

#### 4.3.1 创建 Key (Create)

```csharp
        // ==========================================
        // Key — 创建
        // ==========================================

        /// <summary>
        /// 创建 RSA 密钥
        /// </summary>
        public async Task<KeyVaultKey> CreateRsaKeyAsync(
            string name,
            int keySize = 2048,
            bool hardwareProtected = false,
            Dictionary<string, string> tags = null)
        {
            var options = new CreateRsaKeyOptions(name, hardwareProtected)
            {
                KeySize = keySize
            };
            if (tags != null)
                foreach (var tag in tags)
                    options.Tags[tag.Key] = tag.Value;

            var response = await _keyClient.CreateRsaKeyAsync(options);
            return response.Value;
        }

        /// <summary>
        /// 创建 EC（椭圆曲线）密钥
        /// </summary>
        public async Task<KeyVaultKey> CreateEcKeyAsync(
            string name,
            string curveName = "P-256",
            bool hardwareProtected = false,
            Dictionary<string, string> tags = null)
        {
            var options = new CreateEcKeyOptions(name, hardwareProtected)
            {
                CurveName = curveName switch
                {
                    "P-256" => KeyCurveName.P256,
                    "P-384" => KeyCurveName.P384,
                    "P-521" => KeyCurveName.P521,
                    "P-256K" => KeyCurveName.P256K,
                    _ => KeyCurveName.P256
                }
            };
            if (tags != null)
                foreach (var tag in tags)
                    options.Tags[tag.Key] = tag.Value;

            var response = await _keyClient.CreateEcKeyAsync(options);
            return response.Value;
        }
```

#### 4.3.2 读取 Key (Get)

```csharp
        // ==========================================
        // Key — 读取
        // ==========================================

        /// <summary>
        /// 获取 Key 属性（公钥信息）
        /// </summary>
        public async Task<KeyVaultKey> GetKeyAsync(string name)
        {
            var response = await _keyClient.GetKeyAsync(name);
            return response.Value;
        }

        /// <summary>
        /// 列出所有 Key（不含私钥）
        /// </summary>
        public async Task<List<KeyPropertiesInfo>> ListKeysAsync()
        {
            var result = new List<KeyPropertiesInfo>();
            await foreach (var prop in _keyClient.GetPropertiesOfKeysAsync())
            {
                result.Add(new KeyPropertiesInfo
                {
                    Name = prop.Name,
                    KeyType = prop.KeyType?.ToString(),
                    KeySize = prop.KeySize,
                    Enabled = prop.Enabled,
                    CreatedOn = prop.CreatedOn,
                    UpdatedOn = prop.UpdatedOn,
                    ExpiresOn = prop.ExpiresOn
                });
            }
            return result;
        }
```

#### 4.3.3 Key 加密/解密/签名

```csharp
        // ==========================================
        // Key — 加密/解密/签名
        // ==========================================

        /// <summary>
        /// 使用 Key Vault 密钥加密数据
        /// </summary>
        /// <param name="keyName">密钥名称</param>
        /// <param name="plaintext">明文字节数组</param>
        /// <param name="algorithm">加密算法（默认 RSA-OAEP-256）</param>
        public async Task<byte[]> EncryptAsync(
            string keyName,
            byte[] plaintext,
            string algorithm = "RsaOaep256")
        {
            var cryptoClient = _keyClient
                .GetCryptographyClient(keyName);
            var algo = algorithm switch
            {
                "RsaOaep" => EncryptionAlgorithm.RsaOaep,
                "RsaOaep256" => EncryptionAlgorithm.RsaOaep256,
                "Rsa15" => EncryptionAlgorithm.Rsa15,
                _ => EncryptionAlgorithm.RsaOaep256
            };

            var result = await cryptoClient.EncryptAsync(algo, plaintext);
            return result.Ciphertext;
        }

        /// <summary>
        /// 使用 Key Vault 密钥解密数据
        /// </summary>
        public async Task<byte[]> DecryptAsync(
            string keyName,
            byte[] ciphertext,
            string algorithm = "RsaOaep256")
        {
            var cryptoClient = _keyClient
                .GetCryptographyClient(keyName);
            var algo = algorithm switch
            {
                "RsaOaep" => EncryptionAlgorithm.RsaOaep,
                "RsaOaep256" => EncryptionAlgorithm.RsaOaep256,
                "Rsa15" => EncryptionAlgorithm.Rsa15,
                _ => EncryptionAlgorithm.RsaOaep256
            };

            var result = await cryptoClient.DecryptAsync(algo, ciphertext);
            return result.Plaintext;
        }

        /// <summary>
        /// 使用 Key Vault 密钥签名
        /// </summary>
        public async Task<byte[]> SignAsync(
            string keyName,
            byte[] digest,
            string algorithm = "RS256")
        {
            var cryptoClient = _keyClient
                .GetCryptographyClient(keyName);
            var algo = algorithm switch
            {
                "RS256" => SignatureAlgorithm.RS256,
                "RS384" => SignatureAlgorithm.RS384,
                "RS512" => SignatureAlgorithm.RS512,
                "PS256" => SignatureAlgorithm.PS256,
                "ES256" => SignatureAlgorithm.ES256,
                "ES384" => SignatureAlgorithm.ES384,
                "ES512" => SignatureAlgorithm.ES512,
                _ => SignatureAlgorithm.RS256
            };

            var result = await cryptoClient.SignAsync(algo, digest);
            return result.Signature;
        }

        /// <summary>
        /// 使用 Key Vault 密钥验签
        /// </summary>
        public async Task<bool> VerifyAsync(
            string keyName,
            byte[] digest,
            byte[] signature,
            string algorithm = "RS256")
        {
            var cryptoClient = _keyClient
                .GetCryptographyClient(keyName);
            var algo = algorithm switch
            {
                "RS256" => SignatureAlgorithm.RS256,
                "RS384" => SignatureAlgorithm.RS384,
                "RS512" => SignatureAlgorithm.RS512,
                "PS256" => SignatureAlgorithm.PS256,
                "ES256" => SignatureAlgorithm.ES256,
                "ES384" => SignatureAlgorithm.ES384,
                "ES512" => SignatureAlgorithm.ES512,
                _ => SignatureAlgorithm.RS256
            };

            var result = await cryptoClient.VerifyAsync(
                algo, digest, signature);
            return result.IsValid;
        }
```

#### 4.3.4 删除 Key (Delete)

```csharp
        // ==========================================
        // Key — 删除
        // ==========================================

        /// <summary>
        /// 软删除 Key
        /// </summary>
        public async Task DeleteKeyAsync(string name)
        {
            var operation = await _keyClient
                .StartDeleteKeyAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 恢复已软删除的 Key
        /// </summary>
        public async Task RecoverKeyAsync(string name)
        {
            var operation = await _keyClient
                .StartRecoverDeletedKeyAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 永久清除 Key
        /// </summary>
        public async Task PurgeKeyAsync(string name)
        {
            await _keyClient.PurgeDeletedKeyAsync(name);
        }
```

### 4.4 Certificate 操作（CRUD）

#### 4.4.1 创建/导入 Certificate (Create/Import)

```csharp
        // ==========================================
        // Certificate — 创建/导入
        // ==========================================

        /// <summary>
        /// 创建自签名证书
        /// </summary>
        public async Task<KeyVaultCertificateWithPolicy>
            CreateSelfSignedCertificateAsync(
                string name,
                string subject = "CN=myapp",
                int validityMonths = 12,
                Dictionary<string, string> tags = null)
        {
            var policy = new CertificatePolicy(subject)
            {
                ValidityInMonths = validityMonths,
                KeySize = 2048,
                Exportable = true
            };

            var options = new CertificateCreationOptions(name, policy);
            if (tags != null)
                foreach (var tag in tags)
                    options.Tags[tag.Key] = tag.Value;

            // 自签名无需指定颁发者
            var operation = await _certificateClient
                .StartCreateCertificateAsync(options);
            var response = await operation
                .WaitForCompletionAsync();
            return response.Value;
        }

        /// <summary>
        /// 导入已有证书（PEM / PFX 格式）
        /// </summary>
        public async Task<KeyVaultCertificateWithPolicy>
            ImportCertificateAsync(
                string name,
                byte[] certificateBytes,
                string password = null,
                Dictionary<string, string> tags = null)
        {
            var options = new ImportCertificateOptions(
                name, certificateBytes);
            if (!string.IsNullOrEmpty(password))
                options.Password = password;
            if (tags != null)
                foreach (var tag in tags)
                    options.Tags[tag.Key] = tag.Value;

            var response = await _certificateClient
                .ImportCertificateAsync(options);
            return response.Value;
        }

        /// <summary>
        /// 从 PFX 文件导入证书
        /// </summary>
        public async Task<KeyVaultCertificateWithPolicy>
            ImportCertificateFromFileAsync(
                string name,
                string pfxFilePath,
                string password = null,
                Dictionary<string, string> tags = null)
        {
            if (!File.Exists(pfxFilePath))
                throw new FileNotFoundException(
                    $"证书文件不存在: {pfxFilePath}");

            var bytes = await File.ReadAllBytesAsync(pfxFilePath);
            return await ImportCertificateAsync(
                name, bytes, password, tags);
        }
```

#### 4.4.2 读取 Certificate (Get)

```csharp
        // ==========================================
        // Certificate — 读取
        // ==========================================

        /// <summary>
        /// 获取证书（含公钥，不含私钥）
        /// </summary>
        public async Task<KeyVaultCertificate>
            GetCertificateAsync(string name)
        {
            var response = await _certificateClient
                .GetCertificateAsync(name);
            return response.Value;
        }

        /// <summary>
        /// 获取证书（含私钥，仅在策略允许导出时有效）
        /// </summary>
        public async Task<byte[]>
            GetCertificateWithPrivateKeyAsync(string name)
        {
            var secretName = name;
            try
            {
                var secret = await _secretClient
                    .GetSecretAsync(secretName);
                return Convert.FromBase64String(secret.Value.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // 某些证书的 Secret 名称与证书名称不同
                // Key Vault 会在证书名称后附加后缀
                throw new InvalidOperationException(
                    $"无法获取证书 '{name}' 的私钥。" +
                    $"请确认证书策略允许导出私钥。", ex);
            }
        }

        /// <summary>
        /// 获取证书 CER 格式（仅公钥）
        /// </summary>
        public async Task<byte[]> GetCertificateCerAsync(string name)
        {
            var cert = await GetCertificateAsync(name);
            return cert.Cer;
        }

        /// <summary>
        /// 列出所有证书
        /// </summary>
        public async Task<List<CertificatePropertiesInfo>>
            ListCertificatesAsync()
        {
            var result = new List<CertificatePropertiesInfo>();
            await foreach (var prop in _certificateClient
                .GetPropertiesOfCertificatesAsync())
            {
                result.Add(new CertificatePropertiesInfo
                {
                    Name = prop.Name,
                    Subject = prop.Subject,
                    Issuer = prop.Issuer,
                    Thumbprint = BitConverter
                        .ToString(prop.X509Thumbprint)
                        .Replace("-", "").ToUpper(),
                    CreatedOn = prop.CreatedOn,
                    UpdatedOn = prop.UpdatedOn,
                    ExpiresOn = prop.ExpiresOn,
                    Enabled = prop.Enabled
                });
            }
            return result;
        }
```

#### 4.4.3 删除 Certificate (Delete)

```csharp
        // ==========================================
        // Certificate — 删除
        // ==========================================

        /// <summary>
        /// 软删除证书
        /// </summary>
        public async Task DeleteCertificateAsync(string name)
        {
            var operation = await _certificateClient
                .StartDeleteCertificateAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 恢复已软删除的证书
        /// </summary>
        public async Task RecoverCertificateAsync(string name)
        {
            var operation = await _certificateClient
                .StartRecoverDeletedCertificateAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 永久清除证书
        /// </summary>
        public async Task PurgeCertificateAsync(string name)
        {
            await _certificateClient
                .PurgeDeletedCertificateAsync(name);
        }
```

### 4.5 备份与恢复

```csharp
        // ==========================================
        // 备份与恢复
        // ==========================================

        /// <summary>
        /// 备份 Secret
        /// </summary>
        public async Task<byte[]> BackupSecretAsync(string name)
        {
            var response = await _secretClient
                .BackupSecretAsync(name);
            return response.Value;
        }

        /// <summary>
        /// 从备份恢复 Secret
        /// </summary>
        public async Task RestoreSecretAsync(byte[] backup)
        {
            await _secretClient.RestoreSecretBackupAsync(backup);
        }

        /// <summary>
        /// 备份 Key
        /// </summary>
        public async Task<byte[]> BackupKeyAsync(string name)
        {
            var response = await _keyClient.BackupKeyAsync(name);
            return response.Value;
        }

        /// <summary>
        /// 从备份恢复 Key
        /// </summary>
        public async Task RestoreKeyAsync(byte[] backup)
        {
            await _keyClient.RestoreKeyBackupAsync(backup);
        }

        /// <summary>
        /// 备份 Certificate
        /// </summary>
        public async Task<byte[]> BackupCertificateAsync(string name)
        {
            var response = await _certificateClient
                .BackupCertificateAsync(name);
            return response.Value;
        }

        /// <summary>
        /// 从备份恢复 Certificate
        /// </summary>
        public async Task RestoreCertificateAsync(byte[] backup)
        {
            await _certificateClient
                .RestoreCertificateBackupAsync(backup);
        }

        /// <summary>
        /// 一键备份 Vault 中所有对象到指定目录
        /// </summary>
        public async Task<BackupSummary> BackupAllAsync(
            string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var summary = new BackupSummary();

            // 备份 Secrets
            await foreach (var prop in _secretClient
                .GetPropertiesOfSecretsAsync())
            {
                try
                {
                    var backup = await BackupSecretAsync(prop.Name);
                    var path = Path.Combine(outputDirectory,
                        $"secret_{prop.Name}.bak");
                    await File.WriteAllBytesAsync(path, backup);
                    summary.SecretsBackedUp++;
                }
                catch { summary.SecretsFailed++; }
            }

            // 备份 Keys
            await foreach (var prop in _keyClient
                .GetPropertiesOfKeysAsync())
            {
                try
                {
                    var backup = await BackupKeyAsync(prop.Name);
                    var path = Path.Combine(outputDirectory,
                        $"key_{prop.Name}.bak");
                    await File.WriteAllBytesAsync(path, backup);
                    summary.KeysBackedUp++;
                }
                catch { summary.KeysFailed++; }
            }

            // 备份 Certificates
            await foreach (var prop in _certificateClient
                .GetPropertiesOfCertificatesAsync())
            {
                try
                {
                    var backup = await BackupCertificateAsync(prop.Name);
                    var path = Path.Combine(outputDirectory,
                        $"cert_{prop.Name}.bak");
                    await File.WriteAllBytesAsync(path, backup);
                    summary.CertificatesBackedUp++;
                }
                catch { summary.CertificatesFailed++; }
            }

            return summary;
        }
    }
```

### 4.6 数据模型

```csharp
// ============================================
// 文件: Models/KeyVaultModels.cs
// ============================================

namespace YourProject.Services
{
    // --- Secret 模型 ---

    public class SecretPropertiesInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? UpdatedOn { get; set; }
        public DateTimeOffset? ExpiresOn { get; set; }
        public bool Enabled { get; set; }
        public string ContentType { get; set; }
        public Dictionary<string, string> Tags { get; set; }
            = new Dictionary<string, string>();
    }

    public class SecretVersionInfo
    {
        public string Version { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? UpdatedOn { get; set; }
        public DateTimeOffset? ExpiresOn { get; set; }
        public bool Enabled { get; set; }
    }

    public class DeletedSecretInfo
    {
        public string Name { get; set; }
        public string RecoveryId { get; set; }
        public DateTimeOffset? DeletedOn { get; set; }
        public DateTimeOffset? ScheduledPurgeDate { get; set; }
    }

    // --- Key 模型 ---

    public class KeyPropertiesInfo
    {
        public string Name { get; set; }
        public string KeyType { get; set; }
        public int? KeySize { get; set; }
        public bool Enabled { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? UpdatedOn { get; set; }
        public DateTimeOffset? ExpiresOn { get; set; }
    }

    // --- Certificate 模型 ---

    public class CertificatePropertiesInfo
    {
        public string Name { get; set; }
        public string Subject { get; set; }
        public string Issuer { get; set; }
        public string Thumbprint { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? UpdatedOn { get; set; }
        public DateTimeOffset? ExpiresOn { get; set; }
        public bool Enabled { get; set; }
    }

    // --- 备份模型 ---

    public class BackupSummary
    {
        public int SecretsBackedUp { get; set; }
        public int SecretsFailed { get; set; }
        public int KeysBackedUp { get; set; }
        public int KeysFailed { get; set; }
        public int CertificatesBackedUp { get; set; }
        public int CertificatesFailed { get; set; }

        public int TotalBackedUp => SecretsBackedUp
            + KeysBackedUp + CertificatesBackedUp;
        public int TotalFailed => SecretsFailed
            + KeysFailed + CertificatesFailed;
    }
}
```

---

## 5. Controller 层调用示例

### 5.1 基础 Controller 配置

```csharp
// ============================================
// 文件: Controllers/KeyVaultController.cs
// ============================================

using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Web.Mvc;
using YourProject.Services;

namespace YourProject.Controllers
{
    [Authorize] // 建议添加认证限制
    public class KeyVaultController : Controller
    {
        private readonly KeyVaultService _kv;

        public KeyVaultController()
        {
            string vaultUrl = ConfigurationManager
                .AppSettings["KeyVault:VaultUrl"];
            string tenantId = ConfigurationManager
                .AppSettings["KeyVault:TenantId"];
            _kv = new KeyVaultService(vaultUrl, tenantId);
        }
```

### 5.2 Secret 操作示例

```csharp
        // --- Secret ---

        // 获取 Secret 值
        public async Task<ActionResult> GetSecret(string name)
        {
            try
            {
                var value = await _kv.GetSecretAsync(name);
                return Json(new { success = true, value },
                    JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false,
                    message = ex.Message },
                    JsonRequestBehavior.AllowGet);
            }
        }

        // 设置 Secret
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SetSecret(
            string name, string value)
        {
            var secret = await _kv.SetSecretAsync(
                name, value,
                contentType: "text/plain",
                tags: new Dictionary<string, string>
                {
                    { "Environment", "Production" },
                    { "UpdatedBy", User.Identity.Name }
                });
            return Json(new
            {
                success = true,
                version = secret.Properties.Version,
                message = $"Secret '{name}' 已设置"
            });
        }

        // 列出所有 Secret（管理界面用）
        public async Task<ActionResult> ListSecrets()
        {
            var secrets = await _kv.ListSecretsAsync();
            return View(secrets);
        }

        // 删除 Secret
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteSecret(string name)
        {
            await _kv.DeleteSecretAsync(name);
            return Json(new
            {
                success = true,
                message = $"Secret '{name}' 已删除（90天内可恢复）"
            });
        }
```

### 5.3 Key 操作示例

```csharp
        // --- Key ---

        // 创建 RSA 密钥
        [HttpPost]
        public async Task<ActionResult> CreateRsaKey(
            string name, int keySize = 2048)
        {
            var key = await _kv.CreateRsaKeyAsync(
                name, keySize);
            return Json(new
            {
                success = true,
                keyId = key.Id,
                keyType = key.KeyType,
                keySize = key.Key?.KeySize
            });
        }

        // 加密数据
        [HttpPost]
        public async Task<ActionResult> EncryptData(
            string keyName, string plainText)
        {
            var data = System.Text.Encoding.UTF8
                .GetBytes(plainText);
            var encrypted = await _kv.EncryptAsync(
                keyName, data);
            return Json(new
            {
                success = true,
                ciphertext = Convert.ToBase64String(encrypted)
            });
        }

        // 解密数据
        [HttpPost]
        public async Task<ActionResult> DecryptData(
            string keyName, string cipherBase64)
        {
            var cipher = Convert.FromBase64String(cipherBase64);
            var decrypted = await _kv.DecryptAsync(
                keyName, cipher);
            return Json(new
            {
                success = true,
                plainText = System.Text.Encoding.UTF8
                    .GetString(decrypted)
            });
        }

        // 列出所有 Key
        public async Task<ActionResult> ListKeys()
        {
            var keys = await _kv.ListKeysAsync();
            return View(keys);
        }
```

### 5.4 Certificate 操作示例

```csharp
        // --- Certificate ---

        // 创建自签名证书
        [HttpPost]
        public async Task<ActionResult> CreateCertificate(
            string name, string subject,
            int validityMonths = 12)
        {
            var cert = await _kv
                .CreateSelfSignedCertificateAsync(
                    name, subject, validityMonths);
            return Json(new
            {
                success = true,
                thumbprint = BitConverter
                    .ToString(cert.Cer).Replace("-","")
                    .ToUpper().Substring(0, 20) + "...",
                expiresOn = cert.Properties.ExpiresOn
            });
        }

        // 导入 PFX 证书
        [HttpPost]
        public async Task<ActionResult> ImportCertificate(
            string name, HttpPostedFileBase pfxFile,
            string password)
        {
            if (pfxFile == null || pfxFile.ContentLength == 0)
                return Json(new { success = false,
                    message = "请选择 PFX 文件" });

            using (var ms = new MemoryStream())
            {
                pfxFile.InputStream.CopyTo(ms);
                var cert = await _kv.ImportCertificateAsync(
                    name, ms.ToArray(), password);
                return Json(new
                {
                    success = true,
                    subject = cert.Properties.Subject,
                    thumbprint = BitConverter
                        .ToString(cert.Cer).Replace("-","")
                        .ToUpper().Substring(0, 20) + "..."
                });
            }
        }

        // 列出所有证书
        public async Task<ActionResult> ListCertificates()
        {
            var certs = await _kv.ListCertificatesAsync();
            return View(certs);
        }
```

### 5.5 备份与恢复示例

```csharp
        // --- 备份与恢复 ---

        // 一键备份所有对象
        [HttpPost]
        public async Task<ActionResult> BackupAll()
        {
            string backupDir = Server.MapPath(
                "~/App_Data/KeyVaultBackups/" +
                DateTime.Now:ToString("yyyyMMdd_HHmmss"));
            var summary = await _kv.BackupAllAsync(backupDir);
            return Json(new
            {
                success = true,
                directory = backupDir,
                totalBackedUp = summary.TotalBackedUp,
                totalFailed = summary.TotalFailed,
                secrets = summary.SecretsBackedUp,
                keys = summary.KeysBackedUp,
                certificates = summary.CertificatesBackedUp
            });
        }

        // EF6 数据库上下文使用 Key Vault 连接字符串
        public class AppDbContext : System.Data.Entity.DbContext
        {
            public AppDbContext() : base(GetConnStr()) { }

            private static string GetConnStr()
            {
                var vaultUrl = ConfigurationManager
                    .AppSettings["KeyVault:VaultUrl"];
                var tenantId = ConfigurationManager
                    .AppSettings["KeyVault:TenantId"];
                var svc = new KeyVaultService(vaultUrl, tenantId);
                var value = svc.GetSecretAsync(
                    "Database-ConnectionString")
                    .GetAwaiter().GetResult();
                return value ?? ConfigurationManager
                    .ConnectionStrings["DefaultConnection"]
                    .ConnectionString;
            }
        }
    }
}
```

---

## 6. Web.config 配置

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <!-- Key Vault 配置 -->
    <add key="KeyVault:VaultUrl"
         value="https://my-key-vault-001.vault.azure.net/" />
    <add key="KeyVault:TenantId"
         value="your-tenant-id" />

    <!-- 用户分配的托管标识（可选） -->
    <!-- <add key="KeyVault:ManagedIdentityClientId"
           value="managed-identity-client-id" /> -->
  </appSettings>

  <connectionStrings>
    <!-- 本地降级配置 -->
    <add name="DefaultConnection"
         connectionString="Server=.\SQLEXPRESS;..."
         providerName="System.Data.SqlClient" />
  </connectionStrings>
</configuration>
```

---

## 7. RBAC 权限配置速查

### 7.1 内置角色一览

| 角色 | Secret | Key | Certificate | 适用对象 |
|------|:------:|:---:|:-----------:|---------|
| **Key Vault Secrets User** | 读取 | - | - | Web App（生产） |
| **Key Vault Secrets Officer** | 读写删 | - | - | 运维/DevOps |
| **Key Vault Crypto User** | - | 加密/解密/签名 | - | 需要加解密的应用 |
| **Key Vault Crypto Officer** | - | 完整管理 | - | 密钥管理员 |
| **Key Vault Certificates Officer** | - | - | 完整管理 | 证书管理员 |
| Key Vault Administrator | 全部 | 全部 | 全部 | 紧急修复（慎用） |

### 7.2 CLI 快速分配权限

```bash
# 为 App Service 分配 Secret 读取权限
APP_ID=$(az webapp identity show \
    --resource-group myRG --name my-app \
    --query principalId -o tsv)

az role assignment create \
    --assignee $APP_ID \
    --role "Key Vault Secrets User" \
    --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/my-key-vault-001

# 为当前用户分配 Secret 管理权限（开发环境）
USER_ID=$(az ad signed-in-user show --query id -o tsv)

az role assignment create \
    --assignee $USER_ID \
    --role "Key Vault Secrets Officer" \
    --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/my-key-vault-001
```

### 7.3 生产环境权限矩阵

| 身份 | 角色 | 说明 |
|------|------|------|
| Web App (Managed ID) | Key Vault Secrets User | 只读配置 |
| Azure Function (Managed ID) | Key Vault Secrets User | 只读配置 |
| DevOps 流水线 (SP) | Key Vault Secrets Officer | 部署时写入 |
| 运维工程师 | Key Vault Secrets Officer | 管理配置 |
| 密钥管理员 | Key Vault Crypto Officer | 管理密钥 |
| 证书管理员 | Key Vault Certificates Officer | 管理证书 |
| 安全审计员 | Key Vault Reader | 审计日志 |

---

## 8. 最佳实践与注意事项

### 8.1 应该做的

- **使用 Managed Identity** — 零密钥管理，部署到 Azure 后自动获得身份
- **使用 RBAC 权限模型** — 不要用旧的 Vault Access Policy
- **启用 soft-delete + purge-protection** — 防止误删导致不可逆丢失
- **实现配置降级** — Key Vault 不可用时自动降级到 Web.config
- **Secret 设置过期时间** — 便于追踪和管理
- **使用 Tag 标记** — Environment / Application / Owner 等标签
- **配置网络白名单** — 最小化暴露面
- **定期备份** — 使用 `BackupAllAsync` 定期备份

### 8.2 不应该做的

- 禁止硬编码任何 AK/SK 或 Client Secret
- 禁止为生产应用分配 `Key Vault Administrator` 角色
- 禁止关闭 soft-delete
- 禁止将 Secret 值输出到日志或异常信息中
- 禁止生产环境使用 `InteractiveBrowserCredential`
- 禁止在代码中 catch 后忽略异常不降级

### 8.3 Secret 命名规范

```
推荐格式: {Prefix}-{Type}-{Name}

示例:
  Prod-ConnStr-DefaultConnection
  Prod-AppSetting-EmailSmtpServer
  Prod-ApiKey-Stripe
  Prod-Secret-JwtSigningKey

Prefix: Dev / Test / Staging / Prod
Type:   ConnStr / AppSetting / ApiKey / Secret
```

### 8.4 Key Vault 三种对象选型

| 对比维度 | Secret | Key | Certificate |
|---------|--------|-----|-------------|
| 存储内容 | 字符串（连接串、API Key、密码） | RSA/EC 加密密钥 | X.509 SSL/TLS 证书 |
| 私钥保护 | 由 Key Vault 加密存储 | 私钥永不离开 Vault | 可配置是否允许导出 |
| 典型用途 | 配置管理 | 数据加密、数字签名 | HTTPS、身份认证 |
| 版本管理 | 支持 | 不支持（需手动轮换） | 支持 |
| 自动续期 | 不支持 | 不支持 | 支持（仅证书） |

---

## 9. 常见错误排查

### 9.1 错误码速查

| 错误码 | 含义 | 常见原因 | 解决方案 |
|:------:|------|---------|---------|
| 401 | 未认证 | 未登录 Azure / Managed Identity 未启用 | 本地执行 `az login`；Azure 上启用 Managed Identity |
| 403 | 权限不足 | RBAC 角色未分配或作用域不对 | 检查角色分配，确认作用域正确 |
| 404 | 不存在 | Secret/Key/Certificate 名称拼写错误 | 检查名称，或查看已删除列表 |
| 429 | 限流 | 请求频率过高 | 添加重试逻辑，降低频率 |
| 408 | 超时 | 网络问题或 Key Vault 负载过高 | 检查网络，实现重试 |
| 503 | 服务不可用 | Key Vault 正在维护 | 等待后重试 |

### 9.2 排查清单

```csharp
// 排查步骤 1: 检查认证
// 本地开发执行:
// az login
// 确认输出中有目标订阅

// 排查步骤 2: 检查 RBAC
// az role assignment list --assignee <principal-id> --scope <kv-scope>

// 排查步骤 3: 检查网络
// az keyvault show --name my-key-vault-001 --query properties.networkAcls

// 排查步骤 4: 代码中添加详细日志
try
{
    var value = await kv.GetSecretAsync("MySecret");
}
catch (RequestFailedException ex)
{
    // 记录完整错误信息用于排查
    var statusCode = ex.Status;
    var errorCode = ex.ErrorCode;
    var message = ex.Message;
}
```

### 9.3 .NET Framework 常见兼容问题

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| `DefaultAzureCredential` 找不到认证 | 环境变量/Managed Identity 都未配置 | 本地执行 `az login` |
| 交互式登录报错 | 缺少 `Microsoft.Identity.Client` | `Install-Package Microsoft.Identity.Client` |
| SSL 证书验证失败 | .NET Framework TLS 版本 | 在 Global.asax 中 `ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;` |
| `AsyncVoidMethod` 警告 | 在非 async 方法中调用 async | 使用 `.GetAwaiter().GetResult()` 或 `.Result`（注意死锁） |

---

> 文档结束。如有疑问请联系开发团队。
