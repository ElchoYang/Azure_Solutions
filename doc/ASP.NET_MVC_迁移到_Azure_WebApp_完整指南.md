# ASP.NET MVC Framework 迁移到 Azure Web App 完整指南

> **适用场景**：本地 ASP.NET MVC Framework 应用 + 本地文件系统（按请求 ID / 类型分类）迁移至 Azure Web App + Azure File Storage（REST API 模式，无需 SMB）
>
> **文档版本**：v1.0 — 2026-04-27

---

## 目录

1. [迁移全局概览](#1-迁移全局概览)
2. [迁移前准备](#2-迁移前准备)
3. [Azure Web App 部署迁移](#3-azure-web-app-部署迁移)
4. [文件存储迁移到 Azure File Storage](#4-文件存储迁移到-azure-file-storage)
5. [REST API 访问 Azure File（无 SMB 方案）](#5-rest-api-访问-azure-file无-smb-方案)
6. [本地开发环境配置](#6-本地开发环境配置)
7. [代码改造要点](#7-代码改造要点)
8. [配置管理与密钥安全](#8-配置管理与密钥安全)
9. [文件目录结构规划](#9-文件目录结构规划)
10. [迁移步骤 Checklist](#10-迁移步骤-checklist)
11. [常见问题与注意事项](#11-常见问题与注意事项)

---

## 1. 迁移全局概览

### 1.1 迁移前后对比

| 组件 | 迁移前（本地） | 迁移后（Azure） |
|------|--------------|----------------|
| **Web 应用** | IIS / 本地服务器 | Azure Web App (Windows) |
| **文件存储** | 本地磁盘路径，如 `D:\uploads\{requestId}\{type}\` | Azure File Storage，路径 `/{requestId}/{type}/` |
| **数据库** | 本地 SQL Server | Azure SQL Database（可选，不在本文范围） |
| **配置** | `Web.config` `<appSettings>` | Azure App Service 环境变量 / Azure Key Vault |
| **文件访问方式** | `System.IO` 直接读写 | Azure Storage SDK（REST API） |
| **SMB 挂载** | 不涉及 | **不使用**（企业内部网络受限），改用 REST API / SDK |

### 1.2 架构图

```
┌─────────────────────────────────────────────────────┐
│                    Azure Cloud                       │
│                                                     │
│  ┌──────────────────┐     ┌──────────────────────┐  │
│  │  Azure Web App   │────▶│  Azure File Storage  │  │
│  │ (ASP.NET MVC)    │ SDK │  (REST API 访问)      │  │
│  │  Windows 计划    │     │  /{requestId}/{type}/ │  │
│  └──────────────────┘     └──────────────────────┘  │
│           │                        ▲                 │
│           ▼                        │                 │
│  ┌──────────────────┐     ┌────────────────────┐    │
│  │   Azure Key Vault│     │   Azure Storage     │    │
│  │  (连接字符串等)   │     │   Account           │    │
│  └──────────────────┘     └────────────────────┘    │
└─────────────────────────────────────────────────────┘

本地开发环境:
┌─────────────────────────────────────────────────────┐
│  Visual Studio / VS Code                            │
│  ASP.NET MVC Framework                              │
│  ↓ SDK 访问（同一份代码）                            │
│  Azure Storage Emulator (Azurite) 或 真实 Azure     │
└─────────────────────────────────────────────────────┘
```

---

## 2. 迁移前准备

### 2.1 清单确认

- [ ] 确认 .NET Framework 版本（4.6.2 / 4.7.2 / 4.8）
- [ ] 统计本地文件总量和大小（用于评估迁移时间）
- [ ] 确认文件目录结构规则（按 requestId + type 分类）
- [ ] 确认应用对文件的操作类型：上传、下载、删除、列举
- [ ] 确认是否有定时任务读写文件（Windows Service 或 Quartz）
- [ ] 确认 Azure 订阅权限（需要 Contributor 或 Storage Account Contributor）

### 2.2 需要创建的 Azure 资源

```
Azure 资源组
├── Azure Web App（Windows，.NET Framework 4.x）
├── App Service Plan（建议 B2 及以上）
├── Azure Storage Account（通用 v2，LRS 或 ZRS）
│   └── File Share（名称如 app-files）
└── Azure Key Vault（可选，推荐用于生产）
```

### 2.3 安装工具

```powershell
# Azure CLI（本地上传文件用）
winget install Microsoft.AzureCLI

# Azure Storage Explorer（可视化管理，推荐）
# 下载地址：https://azure.microsoft.com/en-us/features/storage-explorer/

# NuGet 包（项目中添加）
Install-Package Azure.Storage.Files.Shares   # Azure File Storage SDK
Install-Package Azure.Identity               # 托管身份认证（生产推荐）
Install-Package Microsoft.Extensions.Azure   # 可选，DI 集成
```

---

## 3. Azure Web App 部署迁移

### 3.1 创建 Azure Web App

```bash
# 登录 Azure
az login

# 创建资源组
az group create --name rg-myapp --location eastasia

# 创建 App Service Plan（Windows，B2）
az appservice plan create \
  --name asp-myapp \
  --resource-group rg-myapp \
  --sku B2 \
  --is-windows

# 创建 Web App（.NET Framework 4.8）
az webapp create \
  --name myapp-webapp \
  --resource-group rg-myapp \
  --plan asp-myapp \
  --runtime "DOTNET|4.8"
```

### 3.2 部署方式选择

| 方式 | 适合场景 | 说明 |
|------|---------|------|
| **Visual Studio 发布** | 首次部署，快速验证 | 右键项目 → Publish → Azure App Service |
| **Azure DevOps Pipeline** | 持续集成/部署 | 推荐生产环境 |
| **FTP/FTPS** | 小文件修改 | 不推荐大规模部署 |
| **ZIP 部署** | 脚本化 CI/CD | `az webapp deploy` 命令 |

#### 方式一：Visual Studio 发布（最简单）

1. 右键项目 → **发布 (Publish)**
2. 选择 **Azure** → **Azure App Service (Windows)**
3. 登录 Azure 账号，选择刚创建的 Web App
4. 点击 **发布**

#### 方式二：命令行 ZIP 部署

```powershell
# 先在 VS 中发布到本地文件夹
# 然后用 az 命令部署

az webapp deploy `
  --resource-group rg-myapp `
  --name myapp-webapp `
  --src-path ./publish.zip `
  --type zip
```

### 3.3 Web.config 注意事项

Azure Web App 支持 `Web.config`，但以下配置需要调整：

```xml
<!-- 原本地路径配置 — 需要删除或改为 Azure 配置 -->
<!-- <add key="FileBasePath" value="D:\uploads\" /> -->

<!-- 改为从环境变量读取（在 Azure Portal 中配置） -->
<!-- 代码中改为: ConfigurationManager.AppSettings["FileBasePath"] -->
<!-- Azure 中设置环境变量 FileBasePath = /home/site/files（临时）或留空（用 SDK）-->
```

> ⚠️ **重要**：Azure Web App 的本地磁盘（`/home/`）在多实例情况下**不共享**，文件必须迁移到 Azure File Storage。

---

## 4. 文件存储迁移到 Azure File Storage

### 4.1 创建 Storage Account 和 File Share

```bash
# 创建 Storage Account
az storage account create \
  --name mystorageacct001 \
  --resource-group rg-myapp \
  --location eastasia \
  --sku Standard_LRS \
  --kind StorageV2

# 获取连接字符串
az storage account show-connection-string \
  --name mystorageacct001 \
  --resource-group rg-myapp \
  --query connectionString -o tsv

# 创建 File Share
az storage share create \
  --name app-files \
  --account-name mystorageacct001 \
  --quota 100
```

### 4.2 文件目录结构规划

原本地结构迁移到 Azure File Share 后的对应关系：

```
本地磁盘:                          Azure File Share (app-files):
D:\uploads\                        /
  {requestId}\                       {requestId}/
    {type}\                            {type}/
      file.pdf                           file.pdf
      attachment.jpg                     attachment.jpg

示例:
D:\uploads\REQ-20260401-001\PDF\   →  /REQ-20260401-001/PDF/
D:\uploads\REQ-20260401-001\IMG\   →  /REQ-20260401-001/IMG/
```

### 4.3 批量迁移现有文件

#### 方法一：AzCopy（推荐，速度最快）

```powershell
# 下载 AzCopy：https://aka.ms/downloadazcopy-v10-windows

# 登录（推荐用 SAS Token 或 Azure CLI 认证）
.\azcopy login

# 上传整个目录到 File Share
.\azcopy copy `
  "D:\uploads\*" `
  "https://mystorageacct001.file.core.windows.net/app-files/?{SAS_TOKEN}" `
  --recursive=true

# 验证迁移结果
.\azcopy list "https://mystorageacct001.file.core.windows.net/app-files/?{SAS_TOKEN}"
```

#### 方法二：Azure Storage Explorer（可视化）

1. 打开 Azure Storage Explorer
2. 连接到 Storage Account
3. 找到 File Shares → app-files
4. 拖拽本地文件夹上传

#### 方法三：PowerShell 脚本（可控性强）

```powershell
# 安装 Az 模块
Install-Module -Name Az.Storage -Force

# 连接
$ctx = New-AzStorageContext -StorageAccountName "mystorageacct001" `
  -StorageAccountKey "your_key_here"

# 递归上传
$localRoot = "D:\uploads"
$shareName = "app-files"

Get-ChildItem -Path $localRoot -Recurse -File | ForEach-Object {
    $relativePath = $_.FullName.Substring($localRoot.Length + 1)
    $azurePath = $relativePath -replace '\\', '/'
    $dirPath = Split-Path $azurePath -Parent

    # 确保目录存在
    if ($dirPath) {
        New-AzStorageDirectory -Context $ctx -ShareName $shareName `
          -Path $dirPath -ErrorAction SilentlyContinue
    }

    # 上传文件
    Set-AzStorageFileContent -Context $ctx -ShareName $shareName `
      -Source $_.FullName -Path $azurePath -Force

    Write-Host "已上传: $azurePath"
}
```

---

## 5. REST API 访问 Azure File（无 SMB 方案）

> 企业内部网络通常封锁 SMB 协议（端口 445），因此**推荐使用 Azure Storage SDK（内部走 HTTPS/443）**，完全无需 SMB。

### 5.1 为什么不用 SMB？

| 特性 | SMB 挂载 | REST API / SDK |
|------|---------|---------------|
| 协议端口 | **445（常被企业防火墙封锁）** | **443（HTTPS，通常开放）** |
| 代码改动 | 路径无需改，但需挂载 | 需改造 System.IO 调用 |
| Azure Web App 支持 | 支持，但有限制 | **原生支持，推荐** |
| 本地开发 | 依赖网络连通性 | 可用 Azurite 模拟器离线开发 |
| 性能 | 受网络影响大 | SDK 内置重试和连接池 |
| **推荐** | ❌ 企业环境慎用 | ✅ 推荐 |

### 5.2 SDK 核心操作封装

以下是针对「按请求 ID + 类型」目录结构的封装服务类：

```csharp
// AzureFileService.cs — 封装 Azure File Storage 的核心操作
using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using System;
using System.IO;
using System.Threading.Tasks;

public class AzureFileService
{
    private readonly ShareClient _shareClient;

    public AzureFileService(string connectionString, string shareName)
    {
        _shareClient = new ShareClient(connectionString, shareName);
    }

    /// <summary>
    /// 上传文件（按 requestId + type 分类）
    /// </summary>
    public async Task UploadFileAsync(
        string requestId,
        string fileType,
        string fileName,
        Stream fileStream)
    {
        // 构建目录路径：/{requestId}/{fileType}/
        var dirClient = _shareClient
            .GetDirectoryClient(requestId)
            .GetSubdirectoryClient(fileType);

        // 确保目录存在（不存在则创建）
        await EnsureDirectoryExistsAsync(_shareClient.GetDirectoryClient(requestId));
        await EnsureDirectoryExistsAsync(dirClient);

        // 上传文件
        var fileClient = dirClient.GetFileClient(fileName);
        await fileClient.CreateAsync(fileStream.Length);
        await fileClient.UploadAsync(fileStream);
    }

    /// <summary>
    /// 下载文件为 Stream（用于 MVC FileResult）
    /// </summary>
    public async Task<Stream> DownloadFileAsync(
        string requestId,
        string fileType,
        string fileName)
    {
        var fileClient = _shareClient
            .GetDirectoryClient(requestId)
            .GetSubdirectoryClient(fileType)
            .GetFileClient(fileName);

        var download = await fileClient.DownloadAsync();
        return download.Value.Content;
    }

    /// <summary>
    /// 获取文件下载 URL（带 SAS Token，适合前端直接访问）
    /// </summary>
    public Uri GetFileDownloadUrl(
        string requestId,
        string fileType,
        string fileName,
        int expiryMinutes = 30)
    {
        var fileClient = _shareClient
            .GetDirectoryClient(requestId)
            .GetSubdirectoryClient(fileType)
            .GetFileClient(fileName);

        var sasUri = fileClient.GenerateSasUri(
            Azure.Storage.Sas.ShareFileSasPermissions.Read,
            DateTimeOffset.UtcNow.AddMinutes(expiryMinutes));

        return sasUri;
    }

    /// <summary>
    /// 列举某 requestId 下某类型的所有文件
    /// </summary>
    public async Task<System.Collections.Generic.List<string>> ListFilesAsync(
        string requestId,
        string fileType)
    {
        var files = new System.Collections.Generic.List<string>();
        var dirClient = _shareClient
            .GetDirectoryClient(requestId)
            .GetSubdirectoryClient(fileType);

        await foreach (var item in dirClient.GetFilesAndDirectoriesAsync())
        {
            if (!item.IsDirectory)
                files.Add(item.Name);
        }
        return files;
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    public async Task DeleteFileAsync(
        string requestId,
        string fileType,
        string fileName)
    {
        var fileClient = _shareClient
            .GetDirectoryClient(requestId)
            .GetSubdirectoryClient(fileType)
            .GetFileClient(fileName);

        await fileClient.DeleteIfExistsAsync();
    }

    // 内部工具：确保目录存在
    private async Task EnsureDirectoryExistsAsync(ShareDirectoryClient dirClient)
    {
        try
        {
            await dirClient.CreateAsync();
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == "ResourceAlreadyExists")
        {
            // 目录已存在，忽略
        }
    }
}
```

---

## 6. 本地开发环境配置

> 核心思路：本地用 **Azurite**（Azure Storage 模拟器）开发，代码和 Azure 保持一致，只切换连接字符串。

### 6.1 Azurite 模拟器安装（推荐）

```bash
# 方式一：npm 安装（推荐）
npm install -g azurite

# 启动（支持 Blob + Queue + File Share）
azurite --location C:\azurite-data --debug C:\azurite-data\debug.log

# 方式二：VS Code 插件
# 搜索安装：Azurite（Microsoft 官方）
# 快捷键 Ctrl+Shift+P → "Azurite: Start"
```

> **Azurite 默认端口**：
> - Blob: 10000
> - Queue: 10001
> - File: **10002**
>
> Azurite 从 v3.21 开始支持 File Share（Table Share 模拟），完全兼容 SDK。

### 6.2 本地开发连接字符串

```xml
<!-- Web.config — 本地开发 -->
<appSettings>
  <!-- Azurite 本地模拟器固定连接字符串 -->
  <add key="AzureStorageConnectionString"
       value="UseDevelopmentStorage=true" />
  <add key="AzureStorageShareName" value="app-files" />
</appSettings>
```

```xml
<!-- Web.config — 生产环境（从 Azure App Service 环境变量注入，不写入代码）-->
<!-- Azure Portal > App Service > Configuration > Application Settings 中添加 -->
<!-- AzureStorageConnectionString = DefaultEndpointsProtocol=https;... -->
```

### 6.3 多环境配置切换（推荐做法）

```csharp
// ConfigHelper.cs — 统一读取配置
public static class ConfigHelper
{
    // 优先读环境变量（Azure 注入），再读 Web.config
    public static string StorageConnectionString =>
        System.Environment.GetEnvironmentVariable("AzureStorageConnectionString")
        ?? System.Configuration.ConfigurationManager
             .AppSettings["AzureStorageConnectionString"];

    public static string StorageShareName =>
        System.Environment.GetEnvironmentVariable("AzureStorageShareName")
        ?? System.Configuration.ConfigurationManager
             .AppSettings["AzureStorageShareName"]
        ?? "app-files";
}
```

### 6.4 Web.config 环境切换方案（.NET Framework 传统方式）

```xml
<!-- Web.config 主文件 -->
<appSettings file="AppSettings.Local.config">
  <!-- 生产值（会被 Azure App Service 环境变量覆盖） -->
  <add key="AzureStorageConnectionString" value="" />
</appSettings>
```

```xml
<!-- AppSettings.Local.config（加入 .gitignore，不提交到 Git）-->
<appSettings>
  <add key="AzureStorageConnectionString" value="UseDevelopmentStorage=true" />
</appSettings>
```

```gitignore
# .gitignore 中添加
AppSettings.Local.config
```

### 6.5 Azurite 注意事项

| 事项 | 说明 |
|------|------|
| **File Share 支持版本** | Azurite 3.21+ 支持 File Share，请确保版本最新 |
| **SAS Token** | 本地 SAS Token 生成与 Azure 相同，正常可用 |
| **HTTPS** | 本地默认 HTTP，生产用 HTTPS，代码无需区分 |
| **数据持久化** | 重启 Azurite 不丢数据（指定 `--location` 后） |
| **管理工具** | Azure Storage Explorer 可连接本地 Azurite 进行可视化管理 |

---

## 7. 代码改造要点

### 7.1 旧代码（System.IO）→ 新代码（Azure SDK）

#### 上传文件

```csharp
// ===== 旧代码（System.IO）=====
public ActionResult Upload(HttpPostedFileBase file, string requestId, string fileType)
{
    var dir = Path.Combine(ConfigurationManager.AppSettings["FileBasePath"],
                           requestId, fileType);
    Directory.CreateDirectory(dir);
    var savePath = Path.Combine(dir, file.FileName);
    file.SaveAs(savePath);
    return Json(new { success = true });
}

// ===== 新代码（Azure SDK）=====
public async Task<ActionResult> Upload(HttpPostedFileBase file, string requestId, string fileType)
{
    var azureFile = DependencyResolver.Current.GetService<AzureFileService>();
    using (var stream = file.InputStream)
    {
        await azureFile.UploadFileAsync(requestId, fileType, file.FileName, stream);
    }
    return Json(new { success = true });
}
```

#### 下载文件

```csharp
// ===== 旧代码 =====
public ActionResult Download(string requestId, string fileType, string fileName)
{
    var filePath = Path.Combine(ConfigurationManager.AppSettings["FileBasePath"],
                                requestId, fileType, fileName);
    return File(filePath, "application/octet-stream", fileName);
}

// ===== 新代码 =====
public async Task<ActionResult> Download(string requestId, string fileType, string fileName)
{
    var azureFile = DependencyResolver.Current.GetService<AzureFileService>();
    var stream = await azureFile.DownloadFileAsync(requestId, fileType, fileName);
    return File(stream, "application/octet-stream", fileName);
}
```

### 7.2 注册服务（Global.asax 或 DI 容器）

```csharp
// Global.asax.cs 或 UnityConfig.cs（如使用 Unity）
protected void Application_Start()
{
    // ...原有代码

    // 注册 AzureFileService（单例）
    var connStr = ConfigHelper.StorageConnectionString;
    var shareName = ConfigHelper.StorageShareName;
    var fileService = new AzureFileService(connStr, shareName);

    // 如使用 Unity DI
    // container.RegisterInstance<AzureFileService>(fileService);
    // 如不用 DI，可以用静态属性
    FileServiceFactory.Instance = fileService;
}
```

### 7.3 NuGet 包添加（packages.config）

```xml
<!-- packages.config -->
<package id="Azure.Storage.Files.Shares" version="12.20.0" targetFramework="net48" />
<package id="Azure.Core" version="1.44.1" targetFramework="net48" />
<package id="Azure.Identity" version="1.13.2" targetFramework="net48" />
```

```powershell
# 或 Package Manager Console
Install-Package Azure.Storage.Files.Shares -Version 12.20.0
```

---

## 8. 配置管理与密钥安全

### 8.1 连接字符串存储位置对比

| 环境 | 存储位置 | 方式 |
|------|---------|------|
| 本地开发 | `AppSettings.Local.config`（不提交 Git） | Azurite 模拟器 |
| Azure 测试 | App Service → Configuration → Application Settings | 明文，可接受 |
| Azure 生产 | **Azure Key Vault** | 最安全 ✅ |

### 8.2 Azure App Service 环境变量配置

在 Azure Portal 中：

1. 打开 **App Service** → **Configuration** → **Application settings**
2. 添加以下键值对：

```
AzureStorageConnectionString = DefaultEndpointsProtocol=https;AccountName=mystorageacct001;AccountKey=xxx;EndpointSuffix=core.windows.net
AzureStorageShareName        = app-files
```

3. 点击 **Save** → 应用会自动重启

> 这些值会覆盖 `Web.config` 中的同名 `appSettings`，无需修改代码。

### 8.3 Key Vault 方案（生产推荐）

详细文档参见同目录下的 `Azure_KeyVault_NET_Framework_开发文档.md`。

简要步骤：
1. 创建 Key Vault，添加 Secret `AzureStorageConnectionString`
2. 为 App Service 开启系统托管身份（System Managed Identity）
3. 授予 App Service 对 Key Vault 的 `Get Secret` 权限
4. 代码启动时从 Key Vault 读取连接字符串

---

## 9. 文件目录结构规划

### 9.1 推荐目录结构

```
Azure File Share: app-files
│
├── REQ-20260401-001/          ← requestId（可自定义格式）
│   ├── PDF/                   ← fileType
│   │   ├── contract.pdf
│   │   └── invoice.pdf
│   ├── IMG/
│   │   ├── photo1.jpg
│   │   └── photo2.png
│   └── EXCEL/
│       └── report.xlsx
│
├── REQ-20260401-002/
│   └── PDF/
│       └── form.pdf
│
└── _system/                   ← 系统保留目录（可选）
    └── templates/
        └── default.docx
```

### 9.2 文件命名建议

- 避免中文文件名（Azure File 支持，但 URL 编码复杂）
- 统一用 **小写 + 连字符**：`contract-v1.pdf`
- 可在上传时做 `Path.GetFileName()` 清洗，去掉路径注入风险

```csharp
// 安全文件名处理
private string SanitizeFileName(string fileName)
{
    // 只保留文件名，去掉路径
    fileName = Path.GetFileName(fileName);
    // 替换非法字符
    var invalid = Path.GetInvalidFileNameChars();
    foreach (var c in invalid)
        fileName = fileName.Replace(c.ToString(), "_");
    return fileName;
}
```

---

## 10. 迁移步骤 Checklist

### Phase 1：准备阶段（1-2天）

- [ ] 统计现有文件数量和总大小
- [ ] 确认 .NET Framework 版本及第三方包兼容性
- [ ] 创建 Azure 资源（资源组、App Service Plan、Web App、Storage Account、File Share）
- [ ] 本地安装 Azurite，验证 SDK 基本读写

### Phase 2：代码改造（2-5天）

- [ ] 添加 `Azure.Storage.Files.Shares` NuGet 包
- [ ] 实现 `AzureFileService` 封装类
- [ ] 配置 `ConfigHelper`（支持本地 / Azure 双环境）
- [ ] 替换上传逻辑（Controller）
- [ ] 替换下载逻辑（Controller）
- [ ] 替换列举/删除逻辑（如有）
- [ ] 本地用 Azurite 完整测试所有文件操作

### Phase 3：测试部署（1-2天）

- [ ] 部署到 Azure Web App（测试环境）
- [ ] 配置 Azure App Service 环境变量
- [ ] 上传少量测试文件到 Azure File Share
- [ ] 验证上传、下载、列举功能
- [ ] 检查错误日志（Application Insights 或 App Service Log）

### Phase 4：数据迁移（依文件量而定）

- [ ] 用 AzCopy 迁移全量历史文件
- [ ] 抽样验证迁移文件完整性（MD5 校验）
- [ ] 切换 DNS / 域名指向 Azure Web App
- [ ] 保留本地服务器待命 1-2 周（回滚用）

### Phase 5：上线后

- [ ] 监控 Azure Web App 性能指标
- [ ] 设置 Storage Account 的生命周期策略（冷数据自动归档）
- [ ] 配置备份策略（Azure Backup 或定期 AzCopy 到 Blob）
- [ ] 下线本地服务器（确认无问题后）

---

## 11. 常见问题与注意事项

### Q1：Azure Web App 本地磁盘（`/home/`）能直接用吗？

**不推荐**。原因：
- 多实例部署时每个实例的 `/home/` **不共享**（除非挂载 Azure Files，但又依赖 SMB）
- 实例重启后临时文件**会丢失**
- **结论**：一定要使用 Azure File Storage 通过 SDK 访问

### Q2：SMB 挂载在 Azure Web App 上是否可行？

Azure Web App 支持挂载 Azure File Share（SMB），但：
- 需要开放端口 **445**，企业内部防火墙通常封锁
- 只在 Azure 内部网络（VNet）中才可靠
- **推荐替代**：使用 SDK（REST/HTTPS），完全无需 SMB

### Q3：上传大文件时性能差怎么办？

```csharp
// 使用分块上传（UploadAsync 内置，超过 4MB 自动分块）
// 可以配置并行度
var options = new ShareFileUploadOptions
{
    TransferOptions = new Azure.Storage.StorageTransferOptions
    {
        MaximumConcurrency = 4,        // 并发上传块数
        MaximumTransferSize = 4 * 1024 * 1024  // 每块 4MB
    }
};
await fileClient.UploadAsync(stream, options: options);
```

### Q4：如何实现前端直接下载（不经过 Web App）？

使用 **SAS Token** 生成临时下载 URL，前端直接访问 Azure：

```csharp
public ActionResult GetDownloadUrl(string requestId, string fileType, string fileName)
{
    var azureFile = /* 获取 service */;
    var url = azureFile.GetFileDownloadUrl(requestId, fileType, fileName, expiryMinutes: 15);
    return Json(new { url = url.ToString() }, JsonRequestBehavior.AllowGet);
}
```

前端：
```javascript
// 获取临时 URL 后直接跳转
window.location.href = data.url;
```

### Q5：本地 Azurite 和真实 Azure 代码如何保持一致？

只切换连接字符串即可，SDK 代码**完全兼容**：
- 本地：`UseDevelopmentStorage=true`
- Azure：`DefaultEndpointsProtocol=https;AccountName=...`

### Q6：文件迁移后如何验证完整性？

```powershell
# AzCopy 内置 MD5 校验，迁移后用 bench 命令验证
.\azcopy bench "https://mystorageacct001.file.core.windows.net/app-files/?{SAS}"

# 或列举文件对比数量
.\azcopy list "https://mystorageacct001.file.core.windows.net/app-files/?{SAS}" --machine-readable
```

### Q7：错误处理最佳实践

```csharp
try
{
    await fileClient.UploadAsync(stream);
}
catch (RequestFailedException ex)
{
    // ex.ErrorCode：Azure 错误码（如 "ResourceAlreadyExists"、"AuthorizationFailure"）
    // ex.Status：HTTP 状态码（如 404、403、409）
    _logger.Error($"Azure File 上传失败: {ex.ErrorCode} ({ex.Status})", ex);
    throw;
}
```

---

## 附录：参考资源

| 资源 | 链接 |
|------|------|
| Azure File Storage SDK 文档 | https://docs.microsoft.com/azure/storage/files/ |
| Azure.Storage.Files.Shares NuGet | https://www.nuget.org/packages/Azure.Storage.Files.Shares |
| Azurite 模拟器 | https://github.com/Azure/Azurite |
| AzCopy 下载 | https://aka.ms/downloadazcopy-v10-windows |
| Azure Storage Explorer | https://azure.microsoft.com/features/storage-explorer/ |
| 同目录相关文档 | `Azure_File_Share_迁移流程.pdf`、`Azure_KeyVault_NET_Framework_开发文档.md` |

---

*文档生成时间：2026-04-27 | 作者：AI Assistant*
