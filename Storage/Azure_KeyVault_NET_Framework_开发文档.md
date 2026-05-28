# Azure Key Vault .NET Framework 4.7.2 开发文档

> 版本：v2.0 | 更新日期：2026-04-22 | 适用框架：ASP.NET Framework 4.7.2+

---

## 目录

1. [Key Vault 概述](#1-key-vault-概述)
2. [创建 Key Vault](#2-创建-key-vault)
3. [RBAC 权限配置（详细图文）](#3-rbac-权限配置详细图文)
4. [数据初始化到 Key Vault](#4-数据初始化到-key-vault)
5. [.NET Framework 4.7.2 代码实现](#5-net-framework-472-代码实现)
6. [推荐实践与安全规范](#6-推荐实践与安全规范)
7. [常见问题与排查](#7-常见问题与排查)

---

## 1. Key Vault 概述

### 1.1 什么是 Azure Key Vault

Azure Key Vault 是 Azure 提供的集中式机密管理服务，用于安全存储和访问：

- **Secrets（机密）**：连接字符串、API Key、密码等字符串型敏感数据
- **Keys（密钥）**：RSA / EC 非对称加密密钥，支持加密/解密/签名
- **Certificates（证书）**：X.509 SSL/TLS 证书，支持自动续期

### 1.2 核心安全架构

```
┌──────────────────────────────────────────────────────────────┐
│                      安全信任链                               │
│                                                              │
│   应用程序  ──① 身份认证──▶  Azure AD (Entra ID)             │
│                                    │                        │
│                              ② RBAC 授权                    │
│                                    │                        │
│                              ③ 网络隔离                      │
│                                    │                        │
│   应用程序  ◀──④ 返回数据──  Azure Key Vault                │
│                                                              │
│   三层防护：认证（你是谁）→ 授权（你能做什么）→ 网络（你能访问吗）│
└──────────────────────────────────────────────────────────────┘
```

**第一层 - 身份认证**：应用必须通过 Azure AD 证明身份（Managed Identity / Service Principal / 用户登录）

**第二层 - 权限授权（RBAC）**：即使身份通过，也只允许执行被授权的具体操作（读/写/删）

**第三层 - 网络隔离**：即使有权限，也必须从允许的网络位置访问（IP白名单 / 虚拟网络）

### 1.3 对象类型对比

```
Key Vault
│
├── Secrets ──── 存储字符串型数据
│   "Database-ConnectionString"  →  "Server=tcp:mydb..."
│   "ThirdParty-ApiKey"          →  "sk-abc123xyz..."
│   "Admin-Password"             →  "P@ssw0rd!"
│   特点：值始终加密存储，支持版本管理，支持自动过期
│
├── Keys ─────── 存储加密密钥（由 Key Vault 硬件生成和管理）
│   "data-encryption-key"  →  RSA-4096
│   "signing-key"          →  EC-P256
│   特点：私钥永不离开 Key Vault，支持云端加密/解密/签名
│
└── Certificates ── 存储 X.509 证书（含私钥）
    "myapp-ssl-cert"  →  *.example.com, 有效期至 2027-04-22
    特点：支持自动续期、S/MIME、SSL/TLS
```

---

## 2. 创建 Key Vault

### 2.1 方式一：Azure Portal（图形界面）

```
步骤流程：
┌──────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│ 搜索     │───▶│ 基本信息     │───▶│ 安全配置     │───▶│ 审核并创建   │
│ Key Vault│    │ 名称/区域/   │    │ RBAC/网络/   │    │              │
│          │    │ 资源组       │    │ 软删除       │    │              │
└──────────┘    └──────────────┘    └──────────────┘    └──────────────┘
```

**详细步骤**：

**Step 1** - 打开 Azure Portal (portal.azure.com)，顶部搜索栏输入 `Key Vault`

**Step 2** - 点击左侧 `+ Create`，填写基本信息：
```
┌─────────────────────────────────────────────────────┐
│  Project Details                                     │
│  ┌─────────────────────────────────────────────┐    │
│  │ Subscription:    [选择你的订阅 ▼]            │    │
│  │ Resource group:  [选择已有 ▼] 或 [+ New]     │    │
│  └─────────────────────────────────────────────┘    │
│                                                       │
│  Instance Details                                     │
│  ┌─────────────────────────────────────────────┐    │
│  │ Key vault name:  my-key-vault-001           │    │
│  │                  (全局唯一，仅小写+数字+短横)  │    │
│  │ Region:          East Asia                  │    │
│  │ Pricing tier:    Standard                   │    │
│  └─────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
```

**Step 3** - 配置安全选项（关键步骤）：
```
┌─────────────────────────────────────────────────────────────┐
│  🔒 Security & Networking                                     │
│                                                              │
│  Permission model:                                            │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ● Vault access policy    ○ Azure role-based access   │    │
│  │   control (RBAC)                                      │    │
│  │                                                        │    │
│  │   ★ 必须选择 RBAC！不要选 Vault access policy          │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  Recovery options:                                           │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ☑ Enable soft delete        (默认已启用)              │    │
│  │ ☑ Enable purge protection   (★ 必须启用！)           │    │
│  │   Soft-delete retention:    90 days                  │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  Networking:                                                 │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ● Public endpoint (allowed from all networks)        │    │
│  │ ○ Public endpoint (allowed from selected networks)   │    │
│  │ ○ Private endpoint                                   │    │
│  │   ★ 生产环境建议选择 Private endpoint 或限制 IP       │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

> ⚠️ **关键决策**：权限模型必须选择 **Azure role-based access control (RBAC)**，不要选旧的 "Vault access policy"。

**Step 4** - 点击 `Review + Create`，验证通过后点击 `Create`

### 2.2 方式二：Azure CLI

```bash
# 创建 Key Vault（启用 RBAC + 软删除 + 清除保护）
az keyvault create \
  --name my-key-vault-001 \
  --resource-group myResourceGroup \
  --location eastasia \
  --enable-rbac-authorization true \
  --enable-soft-delete true \
  --enable-purge-protection true \
  --retention-days 90 \
  --default-action Deny \
  --bypass AzureServices
```

### 2.3 方式三：PowerShell

```powershell
# 创建 Key Vault
New-AzKeyVault -VaultName "my-key-vault-001" `
    -ResourceGroupName "myResourceGroup" `
    -Location "eastasia" `
    -EnableRbacAuthorization `
    -EnableSoftDelete `
    -EnablePurgeProtection `
    -SoftDeleteRetentionInDays 90 `
    -DefaultAction Deny `
    -Bypass AzureServices

# 验证创建结果
Get-AzKeyVault -VaultName "my-key-vault-001" | Format-List
```

输出确认：
```
Vault Name             : my-key-vault-001
ResourceGroupName      : myResourceGroup
Location               : eastasia
VaultUri               : https://my-key-vault-001.vault.azure.net/
TenantId               : xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
SkuName                : Standard
EnableRbacAuthorization: True
EnableSoftDelete       : True
EnablePurgeProtection  : True
SoftDeleteRetentionDays: 90
```

### 2.4 方式四：ARM / Bicep 模板

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
    softDeleteRetentionInDays: 90
    enableRbacAuthorization: true        // ★ RBAC 模型
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
    accessPolicies: []                   // RBAC 模型下此处为空
  }
}
```

---

## 3. RBAC 权限配置（详细图文）

### 3.1 RBAC 核心概念

RBAC（Role-Based Access Control）的三个要素：

```
┌──────────────┐     ┌──────────────┐     ┌──────────────────────┐
│  Who? 谁来访问 │ ──▶ │ What? 能做什么 │ ──▶ │ Where? 在哪里生效     │
│              │     │              │     │                      │
│ Principal    │     │ Role         │     │ Scope（作用域）        │
│ (安全主体)    │     │ (角色定义)    │     │                      │
│              │     │              │     │ Management Group      │
│ • 用户       │     │ • 内置角色    │     │   ↓                  │
│ • 服务主体   │     │ • 自定义角色  │     │ Subscription          │
│ • 托管标识   │     │              │     │   ↓                  │
│              │     │              │     │ Resource Group       │
│              │     │              │     │   ↓                  │
│              │     │              │     │ Resource (Key Vault) │
└──────────────┘     └──────────────┘     └──────────────────────┘

分配语句："将 [角色] 分配给 [安全主体]，作用于 [作用域]"
```

**关键原则**：
- 权限是**向下继承**的 —— 在订阅级别分配的角色，自动对该订阅下的所有资源组/资源生效
- **更精确的作用域** = **更严格的权限控制** —— 推荐在 Key Vault 资源级别分配
- **拒绝始终优先** —— 如果在某层被拒绝，即使上层有允许也不会生效

### 3.2 内置角色详细说明

#### Secret 相关角色

| 角色 | Secret 列表 | 读取值 | 设置值 | 删除 | 恢复 | 备份 | 清除 |
|------|:-----------:|:------:|:------:|:----:|:----:|:----:|:----:|
| **Key Vault Secrets User** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Key Vault Secrets Officer** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| Key Vault Administrator | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

#### Key 相关角色

| 角色 | Key 列表 | 创建 | 加密/解密 | 签名 | 删除 | 清除 |
|------|:--------:|:----:|:---------:|:----:|:----:|:----:|
| **Key Vault Crypto User** | ✅ | ❌ | ✅ | ✅ | ❌ | ❌ |
| **Key Vault Crypto Officer** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

#### Certificate 相关角色

| 角色 | 证书列表 | 创建/导入 | 管理 | 删除 | 清除 |
|------|:--------:|:---------:|:----:|:----:|:----:|
| Key Vault Certificate User | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Key Vault Certificates Officer** | ✅ | ✅ | ✅ | ✅ | ✅ |

#### 管理角色

| 角色 | 说明 | 能做什么 |
|------|------|----------|
| **Key Vault Contributor** | Key Vault 资源管理员 | 创建/删除 Key Vault 本身、配置网络规则，**但不能访问数据** |
| **Key Vault Reader** | 只读查看者 | 查看 Key Vault 配置和属性，**不能读取数据** |
| Key Vault Administrator | 完全控制 | 所有操作，包括清除保护下的一切权限 |

> 💡 **注意区分**：`Key Vault Contributor` 只能管理 Key Vault **资源本身**（创建/删除 Vault、改网络规则），而 **不能**读取里面的 Secret/Key/Certificate。要访问数据需要分配对应的数据角色。

### 3.3 角色分配 —— 方式一：Azure Portal（图文）

#### 场景 A：为 ASP.NET 应用分配 Secret 读取权限

```
┌──────────────────────────────────────────────────────────────────────┐
│  Step 1: 打开 Key Vault                                             │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  Azure Portal                                                │     │
│  │  🔍 搜索: my-key-vault-001                                  │     │
│  │  → 点击进入 Key Vault 页面                                    │     │
│  └────────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Step 2: 进入权限管理                                                │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  左侧菜单:                                                   │     │
│  │                                                               │     │
│  │  ┌─────────────────┐                                         │     │
│  │  │ 📋 Overview     │                                         │     │
│  │  │ 🔑 Keys        │                                         │     │
│  │  │ 🔒 Secrets      │    ┌──────────────────────────┐        │     │
│  │  │ 📜 Certificates │    │ ★ 点击 Access control     │        │     │
│  │  │ ─────────────── │    │   (IAM)                  │        │     │
│  │  │ 🔐 Access       │    └──────────────────────────┘        │     │
│  │  │   control (IAM) │                                        │     │
│  │  │ 🌐 Networking   │                                        │     │
│  │  └─────────────────┘                                         │     │
│  └────────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Step 3: 添加角色分配                                                │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  Access control (IAM) 页面                                   │     │
│  │                                                               │     │
│  │  ┌─────────────┐                                             │     │
│  │  │ + Add       │  → 点击，选择 "Add role assignment"         │     │
│  │  │ Check access│                                             │     │
│  │  └─────────────┘                                             │     │
│  └────────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Step 4: 选择角色                                                    │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  Job function roles  (跳过此 tab)                            │     │
│  │  Privileged roles  (跳过此 tab)                             │     │
│  │                                                               │     │
│  │  ★ 点击 "Key Vault" 分类                                      │     │
│  │                                                               │     │
│  │  ┌──────────────────────────────────────────────────┐       │     │
│  │  │ 搜索: Secret                                      │       │     │
│  │  │                                                    │       │     │
│  │  │ ☐ Key Vault Administrator                         │       │     │
│  │  │ ☐ Key Vault Certificate User                      │       │     │
│  │  │ ☐ Key Vault Certificates Officer                 │       │     │
│  │  │ ☐ Key Vault Crypto Officer                        │       │     │
│  │  │ ☐ Key Vault Crypto User                          │       │     │
│  │  │ ☐ Key Vault Secrets Officer                      │       │     │
│  │  │ ● Key Vault Secrets User      ← ★ 选择这个！      │       │     │
│  │  │ ☐ Key Vault Reader                                 │       │     │
│  │  └──────────────────────────────────────────────────┘       │     │
│  │                                                               │     │
│  │  点击 "Next"                                                   │     │
│  └────────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Step 5: 选择成员（应用/用户）                                        │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  Members:                                                     │     │
│  │  ● Managed Identity  (推荐)                                  │     │
│  │  ○ User                        │     │
│  │  ○ Group                        │     │
│  │  ○ Service Principal            │     │
│  │                                                               │     │
│  │  ★ 如果是 Azure 托管应用（App Service/VM）：                   │     │
│  │    → 选择 "Managed Identity"                                  │     │
│  │    → "Select members"                                         │     │
│  │    → 找到你的应用名称 → 点击选中                                │     │
│  │                                                               │     │
│  │  ★ 如果是本地开发用户：                                        │     │
│  │    → 选择 "User"                                              │     │
│  │    → 搜索你的用户名 → 点击选中                                  │     │
│  │                                                               │     │
│  │  点击 "Next"                                                   │     │
│  └────────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Step 6: 确认并分配                                                  │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  Review + assign                                             │     │
│  │  ┌──────────────────────────────────────────────────┐       │     │
│  │  │ Role:         Key Vault Secrets User              │       │     │
│  │  │ Assign access to: Managed Identity               │       │     │
│  │  │ Members:      my-web-app                         │       │     │
│  │  │ Description:  应用读取配置所需                      │       │     │
│  │  │                                                    │       │     │
│  │  │           [Review + assign]                        │       │     │
│  │  └──────────────────────────────────────────────────┘       │     │
│  └────────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────┘
```

#### 场景 B：为 DevOps 人员分配 Secret 管理权限

Portal 操作流程相同，在 **Step 4 选择角色** 时改为：

```
┌────────────────────────────────────────────────────────┐
│  ★ 选择 "Key Vault Secrets Officer"                     │
│                                                        │
│  该角色拥有：                                            │
│  • 读取 Secret 值                                      │
│  • 创建/更新 Secret                                     │
│  • 删除 Secret（软删除）                                │
│  • 恢复已删除的 Secret                                  │
│  • 备份 Secret                                         │
│  ✗ 不能永久清除（Purge）                                │
└────────────────────────────────────────────────────────┘
```

#### 场景 C：为 CI/CD 流水线分配权限

```
┌──────────────────────────────────────────────────────────────┐
│  CI/CD 权限分配策略                                           │
│                                                              │
│  ┌───────────────┐    分配角色           ┌───────────────┐   │
│  │ Azure DevOps  │ ────────────────────▶ │ KV Secrets    │   │
│  │ Service       │  Key Vault Secrets   │ Officer       │   │
│  │ Connection    │  Officer             │               │   │
│  └───────────────┘                      └───────────────┘   │
│        │                                                    │
│        │ 使用 Azure DevOps 的 Service                       │
│        │ Connection 连接到 Azure，                          │
│        │ 然后以 Service Principal 身份                       │
│        │ 分配 RBAC 角色                                     │
│                                                              │
│  操作步骤:                                                    │
│  1. Azure DevOps → Project Settings → Service connections    │
│     → New service connection → Azure Resource Manager        │
│  2. 创建完成后，记下 Service Principal 的 Object ID           │
│  3. Key Vault → Access control (IAM) → Add role assignment  │
│     → 选择 "Key Vault Secrets Officer"                       │
│     → Members → 搜索 Service Principal Object ID              │
│  4. 分配角色                                                  │
└──────────────────────────────────────────────────────────────┘
```

### 3.4 角色分配 —— 方式二：Azure CLI

```bash
# ===== 场景 A：为 App Service 分配 Secret 读取权限 =====

# Step 1: 为 Web App 启用系统托管标识
az webapp identity assign \
  --resource-group myResourceGroup \
  --name my-web-app

# Step 2: 获取应用的 Principal ID
APP_ID=$(az webapp identity show \
  --resource-group myResourceGroup \
  --name my-web-app \
  --query principalId -o tsv)

echo "应用 Principal ID: $APP_ID"

# Step 3: 分配角色
az role assignment create \
  --assignee $APP_ID \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001

# 验证分配
az role assignment list \
  --assignee $APP_ID \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001 \
  --output table
```

```bash
# ===== 场景 B：为当前登录用户分配 Secret 管理权限（开发环境） =====

CURRENT_USER=$(az ad signed-in-user show --query id -o tsv)

az role assignment create \
  --assignee $CURRENT_USER \
  --role "Key Vault Secrets Officer" \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001

echo "✅ 已为当前用户分配 Key Vault Secrets Officer 角色"
```

```bash
# ===== 场景 C：为 Service Principal（CI/CD）分配权限 =====

az role assignment create \
  --assignee <service-principal-object-id> \
  --role "Key Vault Secrets Officer" \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001
```

### 3.5 角色分配 —— 方式三：PowerShell

```powershell
# ===== 场景 A：为 App Service 分配 Secret 读取权限 =====

# Step 1: 为 Web App 启用系统托管标识
Set-AzWebApp -ResourceGroupName "myResourceGroup" -Name "my-web-app" -AssignIdentity

# Step 2: 获取应用标识信息
$webapp = Get-AzWebApp -ResourceGroupName "myResourceGroup" -Name "my-web-app"
$principalId = $webapp.Identity.PrincipalId

Write-Host "应用 Principal ID: $principalId"

# Step 3: 分配角色
$scope = "/subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001"

New-AzRoleAssignment `
    -ObjectId $principalId `
    -RoleDefinitionName "Key Vault Secrets User" `
    -Scope $scope

# 验证
Get-AzRoleAssignment `
    -ObjectId $principalId `
    -Scope $scope | Format-Table RoleDefinitionName, ObjectType, DisplayName
```

```powershell
# ===== 场景 B：为当前用户分配 Secret 管理权限 =====

$currentUser = (Get-AzContext).Account.Id

New-AzRoleAssignment `
    -SignInName $currentUser `
    -RoleDefinitionName "Key Vault Secrets Officer" `
    -Scope "/subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001"

Write-Host "✅ 已为当前用户分配 Key Vault Secrets Officer 角色"
```

```powershell
# ===== 场景 C：查看当前 Key Vault 上所有角色分配 =====

$scope = "/subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001"

Get-AzRoleAssignment -Scope $scope | Format-Table `
    RoleDefinitionName, 
    ObjectType, 
    DisplayName, 
    SignInName
```

### 3.6 角色分配 —— 方式四：ARM / Bicep 模板

```bicep
// 为 App Service 托管标识分配 Key Vault Secret 读取权限
@description('App Service 的 Principal ID')
param appPrincipalId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: 'my-key-vault-001'
}

// 分配 Key Vault Secrets User 角色
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appPrincipalId, subscription().id)
  scope: keyVault
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}
```

### 3.7 自定义角色（精细权限控制）

当内置角色粒度不够时，可以创建自定义角色。例如：**只允许读取特定 Secret，不允许列表查看其他 Secret**。

#### Portal 方式创建自定义角色

```
┌──────────────────────────────────────────────────────────────┐
│  Azure Portal 创建自定义角色流程                                │
│                                                              │
│  Step 1: 全局搜索 "Custom roles" (自定义角色)                  │
│                                                              │
│  Step 2: 点击 "+ Create custom role"                         │
│                                                              │
│  Step 3: 基本信息                                             │
│  ┌────────────────────────────────────────────────────┐     │
│  │  Custom role name:  Key Vault Secret Reader Only   │     │
│  │  Description:       仅允许读取指定 Secret           │     │
│  │  Baseline permissions: Start from scratch          │     │
│  └────────────────────────────────────────────────────┘     │
│                                                              │
│  Step 4: 权限配置                                             │
│  ┌────────────────────────────────────────────────────┐     │
│  │  Actions (允许的操作):                                │     │
│  │  ┌──────────────────────────────────────────────┐  │     │
│  │  │ ☑ Microsoft.KeyVault/vaults/secrets/read     │  │     │
│  │  │   (允许读取 Secret 值)                        │  │     │
│  │  └──────────────────────────────────────────────┘  │     │
│  │                                                      │     │
│  │  Not actions (显式拒绝):                              │     │
│  │  ┌──────────────────────────────────────────────┐  │     │
│  │  │ ☑ Microsoft.KeyVault/vaults/secrets/list     │  │     │
│  │  │   (禁止列出所有 Secret)                        │  │     │
│  │  └──────────────────────────────────────────────┘  │     │
│  │                                                      │     │
│  │  ★ 注意：列表权限被显式拒绝后，应用必须知道           │     │
│  │  具体的 Secret 名称才能读取，实现了"盲读"效果        │     │
│  └────────────────────────────────────────────────────┘     │
│                                                              │
│  Step 5: 可分配范围                                          │
│  ┌────────────────────────────────────────────────────┐     │
│  │ Assignable scopes:                                   │     │
│  │ ☑ my-key-vault-001                                  │     │
│  └────────────────────────────────────────────────────┘     │
│                                                              │
│  Step 6: 创建完成后，像内置角色一样分配使用                    │
└──────────────────────────────────────────────────────────────┘
```

#### CLI / PowerShell 方式创建自定义角色

```json
// custom-role-secrets-reader.json
{
  "Name": "Key Vault Secret Reader Only",
  "Description": "仅允许读取指定 Secret，禁止列出所有 Secret",
  "Actions": [
    "Microsoft.KeyVault/vaults/secrets/read"
  ],
  "NotActions": [
    "Microsoft.KeyVault/vaults/secrets/list"
  ],
  "AssignableScopes": [
    "/subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001"
  ]
}
```

```bash
# 创建自定义角色
az role definition create --role-definition custom-role-secrets-reader.json

# 分配自定义角色
az role assignment create \
  --assignee $APP_ID \
  --role "Key Vault Secret Reader Only" \
  --scope /subscriptions/<subscription-id>/resourceGroups/myResourceGroup/providers/Microsoft.KeyVault/vaults/my-key-vault-001
```

### 3.8 权限验证与审计

```powershell
# 查看某个用户在 Key Vault 上的所有权限
Get-AzRoleAssignment `
    -SignInName "user@company.com" `
    -Scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/my-key-vault-001" | Format-List

# 查看某个应用（托管标识）的权限
Get-AzRoleAssignment `
    -ObjectId "<principal-id>" `
    -Scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/my-key-vault-001" | Format-Table RoleDefinitionName

# 模拟测试：检查某用户是否拥有某操作权限（PowerShell）
$definitionId = (Get-AzRoleDefinition -Name "Key Vault Secrets User").Id
Test-AzRoleAssignment `
    -ObjectId "<principal-id>" `
    -RoleDefinitionId $definitionId `
    -Scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/my-key-vault-001"
```

### 3.9 典型权限矩阵（生产环境推荐）

```
┌──────────────────────────────────────────────────────────────────────┐
│                     生产环境权限分配矩阵                               │
│                                                                      │
│  身份                     角色                          原因          │
│  ─────────────────────────────────────────────────────────────       │
│  Web App (Managed ID)      Key Vault Secrets User       只读配置      │
│  API App (Managed ID)      Key Vault Secrets User       只读配置      │
│  Azure Function (Managed)  Key Vault Secrets User       只读配置      │
│                                                                      │
│  DevOps 管线 (SP)           Key Vault Secrets Officer     部署时写入   │
│  运维工程师 (用户)           Key Vault Secrets Officer     管理配置    │
│  DBA (用户)                Key Vault Secrets User        仅查看连接串  │
│                                                                      │
│  密钥管理员 (用户)           Key Vault Crypto Officer      管理密钥    │
│  证书管理员 (用户)           Key Vault Certificates Officer 管理证书   │
│                                                                      │
│  安全审计员 (用户)           Key Vault Reader             审计日志    │
│  基础设施管理员              Key Vault Contributor         管理Vault   │
│  不应存在！                 Key Vault Administrator       ★慎用       │
└──────────────────────────────────────────────────────────────────────┘
```

### 3.10 网络访问控制（第三层防护）

```powershell
# 默认拒绝所有访问
Update-AzKeyVaultNetworkRuleSet -VaultName "my-key-vault-001" -DefaultAction Deny

# 允许特定 IP 地址访问
Add-AzKeyVaultNetworkRule -VaultName "my-key-vault-001" `
    -IpAddressRange "203.0.113.0/24"

# 允许 Azure 受信任服务绕过网络规则
# （如 App Service、Azure Functions、Azure DevOps）
Set-AzKeyVaultNetworkRuleSet -VaultName "my-key-vault-001" `
    -Bypass AzureServices

# 允许虚拟网络内访问
$subnetId = "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Network/virtualNetworks/<vnet>/subnets/<subnet>"
Add-AzKeyVaultNetworkRule -VaultName "my-key-vault-001" -VirtualNetworkResourceId $subnetId

# 查看当前网络规则
Get-AzKeyVaultNetworkRuleSet -VaultName "my-key-vault-001" | Format-List
```

---

## 4. 数据初始化到 Key Vault

在实际项目中，将现有配置数据迁移/初始化到 Key Vault 是常见需求。以下是三种方式：

### 4.1 方式一：Azure Portal 手动创建（适合少量配置）

```
┌──────────────────────────────────────────────────────────────┐
│  Azure Portal 创建 Secret                                     │
│                                                              │
│  Step 1: 打开 Key Vault → 左侧菜单点击 "Secrets"              │
│                                                              │
│  Step 2: 点击 "+ Generate/Import"                             │
│                                                              │
│  Step 3: 填写 Secret 信息                                     │
│  ┌────────────────────────────────────────────────────┐     │
│  │  Upload options:  ● Manual                         │     │
│  │                                                     │     │
│  │  Name:           Database-ConnectionString          │     │
│  │  Value:          Server=tcp:mydb.database.windows.  │     │
│  │                  net,1433;Database=prod;...          │     │
│  │                                                     │     │
│  │  ☑ Set activation date      2026-01-01              │     │
│  │  ☑ Set expiration date      2027-01-01              │     │
│  │                                                     │     │
│  │  Tags:                                              │     │
│  │    Environment: Production                          │     │
│  │    Application: WebApp                             │     │
│  │    Owner: Team-A                                   │     │
│  │                                                     │     │
│  │  Content type:   text/plain                        │     │
│  │                                                     │     │
│  │  Enabled:        ☑ Yes                             │     │
│  │                                                     │     │
│  │  Activation date: (可选) 启用日期                    │     │
│  │  Expiration date:  ★ 建议设置过期时间               │     │
│  │                                                     │     │
│  │                [Create]                             │     │
│  └────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────┘
```

### 4.2 方式二：Azure CLI 批量导入（适合从配置文件迁移）

**场景：将 web.config / appsettings.json 中的连接字符串批量导入 Key Vault**

```bash
# ===== 单个 Secret 导入 =====
az keyvault secret set \
  --vault-name my-key-vault-001 \
  --name "Database-ConnectionString" \
  --value "Server=tcp:mydb.database.windows.net,1433;Database=prod;User ID=admin;Password=xxx;" \
  --tags Environment=Production Application=WebApp

az keyvault secret set \
  --vault-name my-key-vault-001 \
  --name "Redis-ConnectionString" \
  --value "myredis.redis.cache.windows.net:6380,password=xxx,ssl=True" \
  --tags Environment=Production Application=WebApp

# ===== 批量导入（从 JSON 文件） =====
# 准备 secrets.json 文件:
cat > secrets.json << 'EOF'
{
  "secrets": [
    {
      "name": "Database-ConnectionString",
      "value": "Server=tcp:mydb.database.windows.net,1433;Database=prod;...",
      "tags": {"Environment": "Production", "Application": "WebApp"}
    },
    {
      "name": "Redis-ConnectionString",
      "value": "myredis.redis.cache.windows.net:6380,password=xxx,ssl=True",
      "tags": {"Environment": "Production", "Application": "WebApp"}
    },
    {
      "name": "Email-Password",
      "value": "email-password-here",
      "tags": {"Environment": "Production", "Application": "EmailService"}
    },
    {
      "name": "ThirdParty-ApiKey",
      "value": "sk-abc123xyz456",
      "content-type": "application/json",
      "tags": {"Environment": "Production", "Application": "PaymentService"}
    }
  ]
}
EOF

# 使用 jq 批量导入（需要安装 jq）
jq -r '.secrets[] | "az keyvault secret set --vault-name my-key-vault-001 --name \(.name) --value \(.value) --tags \([.tags | to_entries[] | "\(.key)=\(.value)"] | join(" "))"' secrets.json | bash
```

### 4.3 方式三：PowerShell 脚本批量导入（推荐）

**场景：从 Web.config 批量读取 appSettings 和 connectionStrings 导入 Key Vault**

```powershell
# ======================================================
# 文件名: Import-WebConfigToKeyVault.ps1
# 功能: 将 Web.config 中的配置批量导入 Azure Key Vault
# 用法: .\Import-WebConfigToKeyVault.ps1 -VaultName "my-key-vault-001" -ConfigPath "D:\project\Web.config"
# ======================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$VaultName,

    [Parameter(Mandatory=$true)]
    [string]$ConfigPath,

    [Parameter(Mandatory=$false)]
    [string]$Prefix = "",

    [Parameter(Mandatory=$false)]
    [switch]$DryRun  # 只显示不实际执行
)

# 验证文件存在
if (-not (Test-Path $ConfigPath)) {
    Write-Error "配置文件不存在: $ConfigPath"
    exit 1
}

# 加载 Web.config
[xml]$webConfig = Get-Content $ConfigPath

$importedCount = 0
$errorCount = 0

# ===== 导入 connectionStrings =====
Write-Host "`n📋 导入 Connection Strings..." -ForegroundColor Cyan

foreach ($conn in $webConfig.configuration.connectionStrings.add) {
    $secretName = "${Prefix}ConnStr-$($conn.name)"
    $secretValue = $conn.connectionString

    Write-Host "  → $secretName = $($secretValue.Substring(0, [Math]::Min(40, $secretValue.Length)))..." -ForegroundColor Yellow

    if (-not $DryRun) {
        try {
            $secret = Set-AzKeyVaultSecret -VaultName $VaultName -Name $secretName -SecretValue (ConvertTo-SecureString -String $secretValue -AsPlainText -Force) -ContentType "text/plain" -Tag @{ Source = "Web.config"; Type = "ConnectionString" }
            Write-Host "  ✅ 导入成功 (版本: $($secret.Version))" -ForegroundColor Green
            $importedCount++
        }
        catch {
            Write-Host "  ❌ 导入失败: $_" -ForegroundColor Red
            $errorCount++
        }
    }
}

# ===== 导入 appSettings =====
Write-Host "`n📋 导入 App Settings..." -ForegroundColor Cyan

foreach ($setting in $webConfig.configuration.appSettings.add) {
    $secretName = "${Prefix}AppSetting-$($setting.key)"
    $secretValue = $setting.value

    # 跳过空值和非敏感配置
    if ([string]::IsNullOrWhiteSpace($secretValue)) {
        Write-Host "  ⏭️ 跳过（空值）: $secretName" -ForegroundColor DarkGray
        continue
    }

    Write-Host "  → $secretName = $($secretValue.Substring(0, [Math]::Min(40, $secretValue.Length)))..." -ForegroundColor Yellow

    if (-not $DryRun) {
        try {
            $secret = Set-AzKeyVaultSecret -VaultName $VaultName -Name $secretName -SecretValue (ConvertTo-SecureString -String $secretValue -AsPlainText -Force) -ContentType "text/plain" -Tag @{ Source = "Web.config"; Type = "AppSetting" }
            Write-Host "  ✅ 导入成功 (版本: $($secret.Version))" -ForegroundColor Green
            $importedCount++
        }
        catch {
            Write-Host "  ❌ 导入失败: $_" -ForegroundColor Red
            $errorCount++
        }
    }
}

# ===== 导入结果汇总 =====
Write-Host "`n============================================" -ForegroundColor White
Write-Host "导入完成!" -ForegroundColor White
Write-Host "  ✅ 成功: $importedCount" -ForegroundColor Green
Write-Host "  ❌ 失败: $errorCount" -ForegroundColor Red
Write-Host "============================================`n" -ForegroundColor White

# 列出 Vault 中所有 Secret 供确认
Write-Host "📋 当前 Vault 中所有 Secret:" -ForegroundColor Cyan
Get-AzKeyVaultSecret -VaultName $VaultName | ForEach-Object {
    Write-Host "  • $($_.Name) | 创建: $($_.Created) | 启用: $($_.Attributes.Enabled)" -ForegroundColor White
}
```

**使用方法**：

```powershell
# 1. 预览模式（只显示不实际导入）
.\Import-WebConfigToKeyVault.ps1 -VaultName "my-key-vault-001" -ConfigPath "D:\project\Web.config" -Prefix "Prod-" -DryRun

# 2. 确认无误后实际导入
.\Import-WebConfigToKeyVault.ps1 -VaultName "my-key-vault-001" -ConfigPath "D:\project\Web.config" -Prefix "Prod-"
```

### 4.4 方式四：C# 程序批量初始化（适合项目自动化）

```csharp
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Key Vault 数据初始化工具
/// 将 Web.config 中的配置批量同步到 Key Vault
/// </summary>
public class KeyVaultInitializer
{
    private readonly SecretClient _secretClient;

    public KeyVaultInitializer(string vaultUrl, string tenantId)
    {
        var credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { TenantId = tenantId });
        _secretClient = new SecretClient(new Uri(vaultUrl), credential);
    }

    /// <summary>
    /// 从 Web.config 读取配置并初始化到 Key Vault
    /// </summary>
    public async Task<ImportResult> ImportFromWebConfigAsync(
        string webConfigPath,
        string prefix = "",
        bool overwriteExisting = false)
    {
        var result = new ImportResult();

        // 读取 Web.config
        var config = ConfigurationManager.OpenMappedExeConfiguration(
            new ExeConfigurationFileMap { ExeConfigFilename = webConfigPath },
            ConfigurationUserLevel.None);

        // 导入 ConnectionStrings
        foreach (ConnectionStringSettings connStr in config.ConnectionStrings.ConnectionStrings)
        {
            if (string.IsNullOrEmpty(connStr.ConnectionString)) continue;

            var secretName = $"{prefix}ConnStr-{connStr.Name}";
            await ImportSecretAsync(secretName, connStr.ConnectionString, overwriteExisting, result);
        }

        // 导入 AppSettings
        foreach (string key in config.AppSettings.Settings.AllKeys)
        {
            var value = config.AppSettings.Settings[key].Value;
            if (string.IsNullOrEmpty(value)) continue;

            var secretName = $"{prefix}AppSetting-{key}";
            await ImportSecretAsync(secretName, value, overwriteExisting, result);
        }

        return result;
    }

    /// <summary>
    /// 批量设置指定的键值对
    /// </summary>
    public async Task<ImportResult> ImportDictionaryAsync(
        Dictionary<string, string> secrets,
        string prefix = "",
        bool overwriteExisting = false,
        Dictionary<string, string>? tags = null)
    {
        var result = new ImportResult();

        foreach (var kvp in secrets)
        {
            var secretName = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}{kvp.Key}";
            await ImportSecretAsync(secretName, kvp.Value, overwriteExisting, result, tags);
        }

        return result;
    }

    private async Task ImportSecretAsync(
        string name,
        string value,
        bool overwrite,
        ImportResult result,
        Dictionary<string, string>? tags = null)
    {
        try
        {
            // 检查是否已存在
            if (!overwrite)
            {
                try
                {
                    await _secretClient.GetSecretAsync(name);
                    result.Skipped++;
                    Console.WriteLine($"  ⏭️ 跳过（已存在）: {name}");
                    return;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // 不存在，继续创建
                }
            }

            // 创建/更新 Secret
            var secret = new KeyVaultSecret(name, value);
            if (tags != null)
            {
                foreach (var tag in tags)
                    secret.Properties.Tags[tag.Key] = tag.Value;
            }

            var response = await _secretClient.SetSecretAsync(secret);
            result.Succeeded++;
            Console.WriteLine($"  ✅ 导入成功: {name} (版本: {response.Value.Properties.Version})");
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.Errors.Add($"  ❌ 导入失败: {name} - {ex.Message}");
            Console.WriteLine(result.Errors.Last());
        }
    }

    /// <summary>
    /// 验证 Key Vault 中的 Secret 是否可访问
    /// </summary>
    public async Task ValidateSecretsAsync(params string[] secretNames)
    {
        Console.WriteLine("\n🔍 验证 Secret 可访问性...");

        foreach (var name in secretNames)
        {
            try
            {
                var response = await _secretClient.GetSecretAsync(name);
                var displayValue = response.Value.Value;
                if (displayValue.Length > 30)
                    displayValue = displayValue.Substring(0, 30) + "...";
                Console.WriteLine($"  ✅ {name} = {displayValue}");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                Console.WriteLine($"  ❌ {name} - 不存在");
            }
            catch (RequestFailedException ex) when (ex.Status == 403)
            {
                Console.WriteLine($"  🔒 {name} - 权限不足");
            }
        }
    }
}

public class ImportResult
{
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new List<string>();

    public override string ToString()
    {
        return $"导入完成: ✅ 成功={Succeeded}, ❌ 失败={Failed}, ⏭️ 跳过={Skipped}";
    }
}
```

**使用示例（C# 控制台程序）**：

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        var vaultUrl = "https://my-key-vault-001.vault.azure.net/";
        var tenantId = "your-tenant-id";
        var webConfigPath = @"D:\project\Web.config";

        var initializer = new KeyVaultInitializer(vaultUrl, tenantId);

        // 方式一：从 Web.config 批量导入
        Console.WriteLine("=== 从 Web.config 导入 ===");
        var result = await initializer.ImportFromWebConfigAsync(
            webConfigPath,
            prefix: "Prod-",
            overwriteExisting: false);

        Console.WriteLine(result);

        // 方式二：手动指定键值对导入
        Console.WriteLine("\n=== 手动指定键值对导入 ===");
        var secrets = new Dictionary<string, string>
        {
            { "Database-ConnectionString", "Server=tcp:mydb.database.windows.net,1433;Database=prod;User ID=admin;Password=xxx;" },
            { "Redis-ConnectionString", "myredis.redis.cache.windows.net:6380,password=yyy,ssl=True" },
            { "Email-SmtpPassword", "email-password-123" },
            { "Payment-StripeApiKey", "sk_live_abc123xyz" }
        };

        var result2 = await initializer.ImportDictionaryAsync(
            secrets,
            prefix: "Prod-",
            overwriteExisting: true,
            tags: new Dictionary<string, string>
            {
                { "Source", "ManualImport" },
                { "Date", DateTime.UtcNow.ToString("yyyy-MM-dd") }
            });

        Console.WriteLine(result2);

        // 验证导入结果
        await initializer.ValidateSecretsAsync(
            "Prod-Database-ConnectionString",
            "Prod-Redis-ConnectionString",
            "Prod-Email-SmtpPassword",
            "Prod-Payment-StripeApiKey");
    }
}
```

### 4.5 初始化策略对比

| 方式 | 适合场景 | 优点 | 缺点 |
|------|----------|------|------|
| Portal 手动 | 5 个以下 Secret | 简单直观 | 效率低、易出错 |
| Azure CLI | 脚本化/CI/CD | 可复现、支持脚本 | 需要安装 CLI |
| PowerShell | Windows 运维 | .NET 环境无需额外安装 | 需要 Az 模块 |
| C# 程序 | 项目自动化 | 可集成到部署流程 | 开发成本高 |
| ARM/Bicep 模板 | 基础设施即代码 | 版本可控、团队协作 | 不适合已有数据迁移 |

> 💡 **推荐**：日常运维用 **PowerShell 脚本**，CI/CD 流水线用 **Azure CLI**，基础设施用 **Bicep 模板**。

---

## 5. .NET Framework 4.7.2 代码实现

### 5.1 环境准备

#### NuGet 包安装

通过 Visual Studio 的 NuGet Package Manager 控制台执行：

```powershell
# 必需包
Install-Package Azure.Identity -Version 1.13.1
Install-Package Azure.Security.KeyVault.Secrets -Version 4.7.0
Install-Package Azure.Security.KeyVault.Keys -Version 4.7.0
Install-Package Azure.Security.KeyVault.Certificates -Version 4.7.0
```

> ⚠️ **重要兼容性说明**：`Azure.Identity` 和 `Azure.Security.KeyVault.*` 系列 SDK 支持 .NET Standard 2.0，因此可以在 .NET Framework 4.7.2 中使用。但 `DefaultAzureCredential` 在 .NET Framework 下的可用认证类型有限（见下方说明）。

#### .NET Framework 4.7.2 下的认证方式

```
┌──────────────────────────────────────────────────────────────────┐
│  DefaultAzureCredential 在 .NET Framework 4.7.2 下的认证链       │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ 1. EnvironmentCredential                               │    │
│  │    需要设置环境变量:                                      │    │
│  │    AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET│    │
│  │    ★ 适合 CI/CD 环境（Azure DevOps Pipeline）            │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ 2. ManagedIdentityCredential                            │    │
│  │    ★ 仅在 Azure 托管环境可用:                             │    │
│  │    - Azure App Service (需启用 Managed Identity)         │    │
│  │    - Azure Virtual Machine                               │    │
│  │    - Azure Functions                                     │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ 3. VisualStudioCredential                               │    │
│  │    ★ 仅在 Visual Studio IDE 内运行时有效                  │    │
│  │    使用 VS 已登录的 Azure 账户                            │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ 4. AzureCliCredential                                   │    │
│  │    需要预先执行 az login                                  │    │
│  │    ★ 适合本地开发调试                                     │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ 5. AzurePowerShellCredential                            │    │
│  │    需要预先执行 Connect-AzAccount                         │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ 6. InteractiveBrowserCredential                         │    │
│  │    ★ 最后兜底，弹出浏览器让用户登录                       │    │
│  │    注意: .NET Framework 需要额外依赖                      │    │
│  │    Install-Package Microsoft.Identity.Client             │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                   │
│  推荐本地开发方式: az login（最简单）                             │
│  推荐生产部署方式: Managed Identity（最安全）                     │
└──────────────────────────────────────────────────────────────────┘
```

### 5.2 封装：KeyVaultSecretService（核心服务类）

```
📁 项目结构建议:
├── Helpers/
│   └── KeyVaultSecretService.cs     ← 封装所有 Secret 操作
├── App_Start/
│   └── KeyVaultConfig.cs            ← 初始化配置
├── Web.config                        ← Vault URL 等基础配置
└── Controllers/
    └── HomeController.cs             ← 使用示例
```

```csharp
// ============================================
// 文件: Helpers/KeyVaultSecretService.cs
// 功能: Azure Key Vault Secret 操作封装
// 适用于: ASP.NET Framework 4.7.2+
// ============================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace YourProject.Helpers
{
    /// <summary>
    /// Azure Key Vault Secret 操作服务
    /// </summary>
    public class KeyVaultSecretService
    {
        private readonly SecretClient _client;
        private readonly string _vaultUrl;

        /// <summary>
        /// 初始化 Key Vault 客户端
        /// </summary>
        /// <param name="vaultUrl">Key Vault 的 URL，如 https://my-vault.vault.azure.net/</param>
        /// <param name="tenantId">Azure AD Tenant ID（可选，本地开发建议指定）</param>
        /// <param name="managedIdentityClientId">托管标识的 Client ID（可选，指定用户分配的托管标识）</param>
        public KeyVaultSecretService(string vaultUrl, string? tenantId = null, string? managedIdentityClientId = null)
        {
            _vaultUrl = vaultUrl;

            // 配置认证选项
            var options = new DefaultAzureCredentialOptions();

            if (!string.IsNullOrEmpty(tenantId))
            {
                options.TenantId = tenantId;
            }

            if (!string.IsNullOrEmpty(managedIdentityClientId))
            {
                options.ManagedIdentityClientId = managedIdentityClientId;
            }

            // 排除 InteractiveBrowserCredential 以避免 .NET Framework 下的兼容问题
            // 如果需要交互式登录，请显式添加
            options.ExcludeInteractiveBrowserCredential = false;

            var credential = new DefaultAzureCredential(options);
            _client = new SecretClient(new Uri(vaultUrl), credential);
        }

        // ==========================================
        // 读取操作
        // ==========================================

        /// <summary>
        /// 获取 Secret 值（最新版本）
        /// </summary>
        /// <param name="name">Secret 名称</param>
        /// <returns>Secret 值；如果不存在返回 null</returns>
        public async Task<string?> GetSecretAsync(string name)
        {
            try
            {
                var response = await _client.GetSecretAsync(name);
                return response.Value.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Secret 不存在
                return null;
            }
        }

        /// <summary>
        /// 获取 Secret 值（同步版本，便于在非 async 场景使用）
        /// </summary>
        public string? GetSecret(string name)
        {
            return GetSecretAsync(name).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取指定版本的 Secret 值
        /// </summary>
        /// <param name="name">Secret 名称</param>
        /// <param name="version">版本号</param>
        public async Task<string?> GetSecretVersionAsync(string name, string version)
        {
            try
            {
                var response = await _client.GetSecretAsync(name, version);
                return response.Value.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        /// <summary>
        /// 获取所有 Secret 的属性列表（不含实际值，适合管理界面展示）
        /// </summary>
        public async Task<List<SecretInfo>> ListSecretsAsync()
        {
            var result = new List<SecretInfo>();

            await foreach (var properties in _client.GetPropertiesOfSecretsAsync())
            {
                result.Add(new SecretInfo
                {
                    Name = properties.Name,
                    Version = properties.Version,
                    CreatedOn = properties.CreatedOn,
                    UpdatedOn = properties.UpdatedOn,
                    ExpiresOn = properties.ExpiresOn,
                    Enabled = properties.Enabled,
                    Tags = properties.Tags.ToDictionary(t => t.Key, t => t.Value)
                });
            }

            return result;
        }

        // ==========================================
        // 写入操作
        // ==========================================

        /// <summary>
        /// 设置（创建或更新）Secret
        /// 如果 Secret 已存在则创建新版本，不存在则创建
        /// </summary>
        /// <param name="name">Secret 名称</param>
        /// <param name="value">Secret 值</param>
        /// <param name="tags">标签（可选）</param>
        /// <param name="expiresOn">过期时间（可选）</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> SetSecretAsync(
            string name,
            string value,
            Dictionary<string, string>? tags = null,
            DateTimeOffset? expiresOn = null)
        {
            try
            {
                var secret = new KeyVaultSecret(name, value);

                if (expiresOn.HasValue)
                {
                    secret.Properties.ExpiresOn = expiresOn.Value;
                }

                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        secret.Properties.Tags[tag.Key] = tag.Value;
                    }
                }

                var response = await _client.SetSecretAsync(secret);
                return true;
            }
            catch (RequestFailedException ex)
            {
                throw new InvalidOperationException(
                    $"设置 Secret '{name}' 失败 (HTTP {ex.Status}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 批量设置 Secret
        /// </summary>
        public async Task<BatchResult> SetSecretsAsync(
            Dictionary<string, string> secrets,
            Dictionary<string, string>? commonTags = null)
        {
            var result = new BatchResult();

            foreach (var kvp in secrets)
            {
                try
                {
                    await SetSecretAsync(kvp.Key, kvp.Value, commonTags);
                    result.Succeeded.Add(kvp.Key);
                }
                catch (Exception ex)
                {
                    result.Failed.Add(kvp.Key, ex.Message);
                }
            }

            return result;
        }

        // ==========================================
        // 删除操作
        // ==========================================

        /// <summary>
        /// 软删除 Secret（启用 soft-delete 时，90 天内可恢复）
        /// </summary>
        /// <param name="name">Secret 名称</param>
        public async Task DeleteSecretAsync(string name)
        {
            try
            {
                var operation = await _client.StartDeleteSecretAsync(name);
                await operation.WaitForCompletionAsync();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // 已不存在，忽略
            }
        }

        /// <summary>
        /// 恢复已软删除的 Secret
        /// </summary>
        public async Task RecoverSecretAsync(string name)
        {
            var operation = await _client.StartRecoverDeletedSecretAsync(name);
            await operation.WaitForCompletionAsync();
        }

        /// <summary>
        /// 永久删除 Secret（不可恢复！需先软删除且已过保留期）
        /// ★ 谨慎使用！生产环境建议由专人操作
        /// </summary>
        public async Task PurgeSecretAsync(string name)
        {
            try
            {
                await _client.PurgeDeletedSecretAsync(name);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // 已不存在
            }
        }

        // ==========================================
        // 备份与恢复
        // ==========================================

        /// <summary>
        /// 备份 Secret（返回加密的备份字节数组）
        /// </summary>
        public async Task<byte[]> BackupSecretAsync(string name)
        {
            var response = await _client.BackupSecretAsync(name);
            return response.Value;
        }

        /// <summary>
        /// 从备份恢复 Secret
        /// </summary>
        public async Task RestoreSecretAsync(byte[] backupBytes)
        {
            await _client.RestoreSecretBackupAsync(backupBytes);
        }

        // ==========================================
        // 辅助方法
        // ==========================================

        /// <summary>
        /// 检查 Secret 是否存在
        /// </summary>
        public async Task<bool> SecretExistsAsync(string name)
        {
            try
            {
                await _client.GetSecretAsync(name);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
        }
    }

    // ==========================================
    // 辅助类
    // ==========================================

    /// <summary>
    /// Secret 属性信息（不含值）
    /// </summary>
    public class SecretInfo
    {
        public string Name { get; set; } = "";
        public string? Version { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? UpdatedOn { get; set; }
        public DateTimeOffset? ExpiresOn { get; set; }
        public bool Enabled { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>
    /// 批量操作结果
    /// </summary>
    public class BatchResult
    {
        public List<string> Succeeded { get; set; } = new();
        public Dictionary<string, string> Failed { get; set; } = new();
        public int TotalSucceeded => Succeeded.Count;
        public int TotalFailed => Failed.Count;
        public bool AllSucceeded => Failed.Count == 0;

        public override string ToString()
        {
            return $"成功: {TotalSucceeded}, 失败: {TotalFailed}";
        }
    }
}
```

### 5.3 初始化配置（Global.asax.cs）

```csharp
// ============================================
// 文件: Global.asax.cs
// 功能: 应用启动时初始化 Key Vault 服务
// ============================================

using System;
using System.Configuration;
using System.Web;
using YourProject.Helpers;

namespace YourProject
{
    public class MvcApplication : HttpApplication
    {
        /// <summary>
        /// 全局 Key Vault 服务实例
        /// </summary>
        public static KeyVaultSecretService? KeyVaultService { get; private set; }

        protected void Application_Start()
        {
            // 初始化 Key Vault
            InitializeKeyVault();

            // 其他初始化逻辑...
            // AreaRegistration.RegisterAllAreas();
            // RouteConfig.RegisterRoutes(RouteTable.Routes);
        }

        private void InitializeKeyVault()
        {
            try
            {
                // 从 Web.config 读取 Key Vault 配置
                var vaultUrl = ConfigurationManager.AppSettings["KeyVault:VaultUrl"];
                var tenantId = ConfigurationManager.AppSettings["KeyVault:TenantId"];
                var managedIdentityClientId = ConfigurationManager.AppSettings["KeyVault:ManagedIdentityClientId"];

                if (string.IsNullOrEmpty(vaultUrl))
                {
                    // Key Vault 未配置，跳过初始化
                    System.Diagnostics.Trace.TraceWarning("Key Vault 未配置，将使用 Web.config 作为配置源");
                    return;
                }

                // 创建 Key Vault 服务实例
                KeyVaultService = new KeyVaultSecretService(
                    vaultUrl: vaultUrl,
                    tenantId: tenantId,
                    managedIdentityClientId: managedIdentityClientId);

                // 验证连接（可选，启动时测试一次）
                var testSecret = KeyVaultService.GetSecret("__health-check");
                if (testSecret != null)
                {
                    System.Diagnostics.Trace.TraceInformation("✅ Key Vault 连接成功");
                }
            }
            catch (Exception ex)
            {
                // Key Vault 初始化失败不应该阻止应用启动
                // 记录错误，降级到 Web.config
                System.Diagnostics.Trace.TraceError($"Key Vault 初始化失败: {ex.Message}");
                System.Diagnostics.Trace.TraceWarning("降级使用 Web.config 配置");
                KeyVaultService = null;
            }
        }
    }
}
```

### 5.4 Web.config 配置

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <!-- Key Vault 配置 -->
    <add key="KeyVault:VaultUrl" value="https://my-key-vault-001.vault.azure.net/" />
    <add key="KeyVault:TenantId" value="your-tenant-id" />
    <!-- 如果使用用户分配的托管标识，需要指定 Client ID -->
    <!-- <add key="KeyVault:ManagedIdentityClientId" value="managed-identity-client-id" /> -->

    <!-- 以下为本地 fallback 配置（Key Vault 不可用时降级使用） -->
    <add key="Database:ConnectionString" value="Server=.\SQLEXPRESS;Database=MyDB;Trusted_Connection=True;" />
  </appSettings>

  <connectionStrings>
    <!-- 本地 fallback（Key Vault 不可用时降级使用） -->
    <add name="DefaultConnection" connectionString="Server=.\SQLEXPRESS;Database=MyDB;Trusted_Connection=True;" providerName="System.Data.SqlClient" />
  </connectionStrings>
</configuration>
```

### 5.5 Controller 中使用

```csharp
// ============================================
// 文件: Controllers/HomeController.cs
// 使用示例
// ============================================

using System.Web.Mvc;
using YourProject.Helpers;

namespace YourProject.Controllers
{
    public class HomeController : Controller
    {
        // GET: /
        public ActionResult Index()
        {
            // 方式一：使用全局服务
            var connectionString = GetConnectionString("Database-ConnectionString");
            ViewBag.ConnectionString = connectionString;

            var apiKey = GetSecret("ThirdParty-ApiKey");
            ViewBag.ApiKeyConfigured = !string.IsNullOrEmpty(apiKey);

            return View();
        }

        // GET: /Admin/Secrets
        /// <summary>
        /// 管理页面：查看所有 Secret（需要管理员权限）
        /// </summary>
        public ActionResult ListSecrets()
        {
            var service = MvcApplication.KeyVaultService;
            if (service == null)
            {
                return Content("Key Vault 未配置");
            }

            var secrets = service.ListSecretsAsync().GetAwaiter().GetResult();
            return View(secrets);
        }

        // POST: /Admin/Secrets/Set
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetSecret(string name, string value)
        {
            var service = MvcApplication.KeyVaultService;
            if (service == null)
            {
                return Json(new { success = false, message = "Key Vault 未配置" });
            }

            try
            {
                service.SetSecretAsync(name, value).GetAwaiter().GetResult();
                return Json(new { success = true, message = $"Secret '{name}' 已设置" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Admin/Secrets/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteSecret(string name)
        {
            var service = MvcApplication.KeyVaultService;
            if (service == null)
            {
                return Json(new { success = false, message = "Key Vault 未配置" });
            }

            try
            {
                service.DeleteSecretAsync(name).GetAwaiter().GetResult();
                return Json(new { success = true, message = $"Secret '{name}' 已删除（90天内可恢复）" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // 辅助方法：带降级的配置读取
        // ==========================================

        /// <summary>
        /// 读取 Secret，Key Vault 不可用时降级到 Web.config
        /// </summary>
        private string? GetSecret(string secretName)
        {
            try
            {
                var service = MvcApplication.KeyVaultService;
                if (service != null)
                {
                    var value = service.GetSecret(secretName);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"从 Key Vault 读取 '{secretName}' 失败: {ex.Message}");
            }

            // 降级到 Web.config
            return System.Configuration.ConfigurationManager.AppSettings[secretName];
        }

        /// <summary>
        /// 读取连接字符串，Key Vault 不可用时降级到 Web.config
        /// </summary>
        private string? GetConnectionString(string name)
        {
            try
            {
                var service = MvcApplication.KeyVaultService;
                if (service != null)
                {
                    var value = service.GetSecret($"ConnStr-{name}");
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"从 Key Vault 读取连接串 '{name}' 失败: {ex.Message}");
            }

            // 降级到 Web.config
            return System.Configuration.ConfigurationManager.ConnectionStrings[name]?.ConnectionString;
        }
    }
}
```

### 5.6 管理视图（可选，Secret 管理界面）

```html
@* 文件: Views/Home/ListSecrets.cshtml *@
@model List<YourProject.Helpers.SecretInfo>

@{
    ViewBag.Title = "Key Vault Secrets 管理";
    Layout = "~/Views/Shared/_Layout.cshtml";
}

<div class="container">
    <h2>🔑 Key Vault Secrets</h2>

    <!-- 新增 Secret -->
    <div class="panel panel-default" style="margin-bottom: 20px;">
        <div class="panel-heading">
            <strong>新增 / 更新 Secret</strong>
        </div>
        <div class="panel-body">
            <div class="form-inline">
                <div class="form-group" style="margin-right: 10px;">
                    <input type="text" id="secretName" class="form-control" placeholder="Secret 名称" />
                </div>
                <div class="form-group" style="margin-right: 10px;">
                    <input type="password" id="secretValue" class="form-control" placeholder="Secret 值" style="width: 300px;" />
                </div>
                <button type="button" class="btn btn-primary" onclick="setSecret()">设置</button>
            </div>
        </div>
    </div>

    <!-- Secret 列表 -->
    <table class="table table-striped table-hover">
        <thead>
            <tr>
                <th>名称</th>
                <th>版本</th>
                <th>创建时间</th>
                <th>更新时间</th>
                <th>过期时间</th>
                <th>状态</th>
                <th>标签</th>
                <th>操作</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var secret in Model)
            {
                <tr>
                    <td><code>@secret.Name</code></td>
                    <td><small>@(secret.Version?.Substring(0, 8))...</small></td>
                    <td>@secret.CreatedOn?.ToLocalTime()</td>
                    <td>@secret.UpdatedOn?.ToLocalTime()</td>
                    <td>
                        @if (secret.ExpiresOn.HasValue)
                        {
                            if (secret.ExpiresOn < DateTimeOffset.UtcNow)
                            {
                                <span class="label label-danger">已过期</span>
                            }
                            else if (secret.ExpiresOn < DateTimeOffset.UtcNow.AddDays(30))
                            {
                                <span class="label label-warning">即将过期</span>
                            }
                            else
                            {
                                <span class="label label-success">@secret.ExpiresOn.Value.ToLocalTime()</span>
                            }
                        }
                        else
                        {
                            <span class="label label-default">永不过期</span>
                        }
                    </td>
                    <td>
                        @if (secret.Enabled)
                        {
                            <span class="label label-success">启用</span>
                        }
                        else
                        {
                            <span class="label label-danger">禁用</span>
                        }
                    </td>
                    <td>
                        @foreach (var tag in secret.Tags)
                        {
                            <span class="label label-info" style="margin-right: 3px;">@tag.Key=@tag.Value</span>
                        }
                    </td>
                    <td>
                        <button type="button" class="btn btn-danger btn-sm"
                                onclick="deleteSecret('@secret.Name')">
                            删除
                        </button>
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>

@section Scripts {
    <script>
        function setSecret() {
            var name = $('#secretName').val();
            var value = $('#secretValue').val();
            if (!name || !value) { alert('请填写名称和值'); return; }

            // 获取防伪令牌
            var token = $('input[name="__RequestVerificationToken"]').val();

            $.ajax({
                url: '@Url.Action("SetSecret")',
                type: 'POST',
                data: {
                    __RequestVerificationToken: token,
                    name: name,
                    value: value
                },
                success: function (result) {
                    alert(result.message);
                    if (result.success) location.reload();
                },
                error: function (xhr) {
                    alert('操作失败: ' + xhr.responseText);
                }
            });
        }

        function deleteSecret(name) {
            if (!confirm('确定要删除 "' + name + '" 吗？（90天内可恢复）')) return;

            var token = $('input[name="__RequestVerificationToken"]').val();

            $.ajax({
                url: '@Url.Action("DeleteSecret")',
                type: 'POST',
                data: {
                    __RequestVerificationToken: token,
                    name: name
                },
                success: function (result) {
                    alert(result.message);
                    if (result.success) location.reload();
                },
                error: function (xhr) {
                    alert('操作失败: ' + xhr.responseText);
                }
            });
        }
    </script>
}
```

### 5.7 EF / ADO.NET 中使用 Key Vault 连接字符串

```csharp
// ============================================
// EF6 / ADO.NET 中使用 Key Vault 获取连接字符串
// ============================================

using System.Data.Entity;
using YourProject.Helpers;

namespace YourProject.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext() : base(GetConnectionString("DefaultConnection")) { }

        public DbSet<User> Users { get; set; }
        // ... 其他 DbSet

        /// <summary>
        /// 从 Key Vault 获取连接字符串，降级到 Web.config
        /// </summary>
        private static string GetConnectionString(string name)
        {
            try
            {
                var service = MvcApplication.KeyVaultService;
                if (service != null)
                {
                    var value = service.GetSecret($"ConnStr-{name}");
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Key Vault 连接串获取失败: {ex.Message}");
            }

            // 降级到 Web.config
            return System.Configuration.ConfigurationManager.ConnectionStrings[name]?.ConnectionString
                   ?? throw new InvalidOperationException($"连接字符串 '{name}' 未配置");
        }
    }
}
```

---

## 6. 推荐实践与安全规范

### 6.1 推荐架构

```
┌──────────────────────────────────────────────────────────────────────┐
│  ASP.NET Framework 4.7.2 应用                                         │
│                                                                      │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  Global.asax.cs                                               │  │
│  │  Application_Start() → 初始化 KeyVaultSecretService            │  │
│  │  (本地开发: az login, 生产: Managed Identity)                  │  │
│  └────────────────────────┬──────────────────────────────────────┘  │
│                           │                                          │
│  ┌────────────────────────▼──────────────────────────────────────┐  │
│  │  KeyVaultSecretService (全局单例)                              │  │
│  │  GetSecret(name) → Key Vault 或 Web.config 降级               │  │
│  └────────────────────────┬──────────────────────────────────────┘  │
│                           │                                          │
│           ┌───────────────┼───────────────┐                          │
│           ▼               ▼               ▼                          │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐                   │
│  │ Controller │  │  EF / ADO  │  │  Service   │                   │
│  │ GetSecret()│  │ 连接字符串  │  │   层       │                   │
│  └────────────┘  └────────────┘  └────────────┘                   │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│  Azure                                                              │
│  ┌────────────┐    RBAC     ┌────────────┐    网络规则    ┌────────┐│
│  │ Managed ID │ ──────────▶ │ Key Vault  │ ◀─────────── │ 防火墙  ││
│  └────────────┘             │  Secrets   │              │        ││
│                             └────────────┘              └────────┘│
└──────────────────────────────────────────────────────────────────────┘
```

### 6.2 ✅ 应该做的

1. **使用 Managed Identity** —— 零密钥管理，应用部署到 Azure 后自动获得身份
2. **使用 RBAC 权限模型** —— 不要用旧的 Vault Access Policy
3. **启用 soft-delete + purge-protection** —— 防止误删导致不可逆丢失
4. **实现配置降级** —— Key Vault 不可用时自动降级到 Web.config
5. **Secret 设置过期时间** —— 便于追踪和管理
6. **使用 Tag 标记** —— Environment / Application / Owner 等标签
7. **配置网络白名单** —— 最小化暴露面
8. **本地开发使用 `az login`** —— 最简单的认证方式

### 6.3 ❌ 不应该做的

1. 禁止硬编码任何 AK/SK 或 Client Secret
2. 禁止为生产应用分配 `Key Vault Administrator` 角色
3. 禁止关闭 soft-delete
4. 禁止将 Secret 值输出到日志或异常信息中
5. 禁止在代码中直接 catch 后忽略异常不降级
6. 禁止生产环境使用 `InteractiveBrowserCredential`

### 6.4 Secret 命名规范

```
推荐格式: {Prefix}-{Type}-{Name}

示例:
┌────────────────────────────────────────────────┐
│  Prod-ConnStr-DefaultConnection                 │
│  Prod-ConnStr-Redis                             │
│  │     │       │                                │
│  │     │       └── 具体名称                     │
│  │     └── 类型 (ConnStr / AppSetting / ApiKey) │
│  └── 环境前缀 (Dev / Test / Staging / Prod)     │
├────────────────────────────────────────────────┤
│  Prod-AppSetting-EmailSmtpServer                │
│  Prod-AppSetting-LogPath                        │
│  Prod-ApiKey-Stripe                             │
│  Prod-ApiKey-SendGrid                           │
│  Prod-Secret-JwtSigningKey                      │
└────────────────────────────────────────────────┘
```

---

## 7. 常见问题与排查

### 7.1 错误码速查

| 错误码 | 含义 | 常见原因 | 解决方案 |
|--------|------|----------|----------|
| 401 | 未认证 | 未登录 Azure / Managed Identity 未启用 | 本地执行 `az login`；Azure 上启用 Managed Identity |
| 403 | 权限不足 | RBAC 角色未分配或角色不对 | 检查角色分配，确认作用域正确 |
| 404 | 不存在 | Secret 名称拼写错误 | 检查名称，或查看已删除列表 |
| 429 | 请求超频 | 超过 API 限制 | 实现重试（Exponential Backoff） |
| 403 (Network) | 网络拒绝 | IP/VNet 不在白名单 | 添加防火墙规则 |

### 7.2 排查脚本（PowerShell）

```powershell
# ===== Key Vault 全链路排查 =====

$vaultName = "my-key-vault-001"
$rg = "myResourceGroup"

Write-Host "===== 1. Key Vault 基本状态 =====" -ForegroundColor Cyan
$vault = Get-AzKeyVault -VaultName $vaultName -ResourceGroupName $rg
Write-Host "  Vault URL: $($vault.VaultUri)"
Write-Host "  RBAC 模式: $($vault.EnableRbacAuthorization)"
Write-Host "  Soft Delete: $($vault.EnableSoftDelete)"
Write-Host "  Purge Protection: $($vault.EnablePurgeProtection)"

Write-Host "`n===== 2. 当前用户身份 =====" -ForegroundColor Cyan
$ctx = Get-AzContext
Write-Host "  账户: $($ctx.Account.Id)"
Write-Host "  订阅: $($ctx.Subscription.Name)"
Write-Host "  Tenant: $($ctx.Tenant.Id)"

Write-Host "`n===== 3. 当前用户的 KV 权限 =====" -ForegroundColor Cyan
$scope = $vault.ResourceId
Get-AzRoleAssignment -Scope $scope | Where-Object {
    $_.ObjectId -eq (Get-AzADUser -UserPrincipalName $ctx.Account.Id).Id
} | Format-Table RoleDefinitionName, Scope

Write-Host "`n===== 4. 所有角色分配 =====" -ForegroundColor Cyan
Get-AzRoleAssignment -Scope $scope | Format-Table RoleDefinitionName, ObjectType, DisplayName

Write-Host "`n===== 5. Secret 列表 =====" -ForegroundColor Cyan
Get-AzKeyVaultSecret -VaultName $vaultName | Format-Table Name, Enabled, Created, Expires

Write-Host "`n===== 6. 网络规则 =====" -ForegroundColor Cyan
Get-AzKeyVaultNetworkRuleSet -VaultName $vaultName | Format-List

Write-Host "`n===== 7. 测试 Secret 读取 =====" -ForegroundColor Cyan
try {
    $secret = Get-AzKeyVaultSecret -VaultName $vaultName -Name "Database-ConnectionString" -ErrorAction Stop
    Write-Host "  ✅ Secret 读取成功" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Secret 读取失败: $($_.Exception.Message)" -ForegroundColor Red
}
```

### 7.3 .NET Framework 常见问题

**Q: `DefaultAzureCredential` 报 "No suitable credential found"**

```
原因: .NET Framework 下没有找到可用的认证凭据

解决方案（按优先级）:
1. 本地开发 → 打开命令行执行 az login
2. CI/CD → 设置环境变量:
   set AZURE_TENANT_ID=xxx
   set AZURE_CLIENT_ID=xxx
   set AZURE_CLIENT_SECRET=xxx
3. Azure 托管 → 确认 Managed Identity 已启用:
   az webapp identity show -g myRG -n myApp
```

**Q: 在 Visual Studio 中调试时无法连接 Key Vault**

```
解决方案:
1. 确认 Visual Studio 已登录 Azure 账户
2. 工具 → 选项 → Azure Service Authentication → 选择 "Account"
3. 或者在命令行执行 az login 后重启 Visual Studio
```

**Q: 如何在 .NET Framework 中使用用户分配的 Managed Identity**

```csharp
// 指定用户分配的托管标识 Client ID
var options = new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = "your-managed-identity-client-id"
};
var credential = new DefaultAzureCredential(options);
```

---

*本文档基于 Azure SDK 4.7.x 编写，适用于 ASP.NET Framework 4.7.2+。*
*RBAC 配置支持 Portal / CLI / PowerShell / Bicep 四种方式。*
