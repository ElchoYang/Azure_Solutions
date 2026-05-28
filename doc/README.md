# Azure File Share 操作指南

基于 ASP.NET Framework MVC 的 Azure Storage File Share 完整解决方案

> 📖 **配套文档**：
> - `Azure_Storage_四种存储类型介绍.pdf` — Azure Storage Account 四种存储服务详解（20+页，含选型指南、场景推荐）
> - `Azure_File_Share_迁移流程.pdf` — 本地文件系统迁移到 Azure 的完整操作指南（25页，含 3 种迁移方案 + MVC 集成 + PowerShell 脚本 + 验证回滚）
> - `Azure_File_Share_介绍.docx` — Azure File Storage 架构介绍（含与 Windows 文件系统对比、目录模型详解）
> - `Azure_File_Share_操作手册.docx` — 完整操作手册

## 目录

- [Azure File Storage 核心概念](#azure-file-storage-核心概念)
- [与 Windows 文件系统的区别](#与-windows-文件系统的区别)
- [项目概述](#项目概述)
- [功能特性](#功能特性)
- [快速开始](#快速开始)
- [配置说明](#配置说明)
- [API 参考](#api-参考)
- [代码示例](#代码示例)
- [常见问题](#常见问题)

---

## Azure File Storage 核心概念

### 什么是 Azure File Storage？

Azure File Storage 是微软 Azure 提供的**全托管云端文件共享服务**，基于 SMB 和 NFS 协议，可以像映射网络驱动器一样使用，也可以通过 REST API / SDK 访问。

**层级结构：**

```
Azure 订阅 (Subscription)
  └── 资源组 (Resource Group)
        └── 存储账户 (Storage Account)  ← 全局唯一
              └── 文件共享 (File Share)  ← 类似网络共享根目录
                    ├── 目录 (Directory)  ← 虚拟目录（无物理实体）
                    │     └── 子目录...
                    └── 文件 (File)       ← 实际数据
```

---

## 与 Windows 文件系统的区别

### ✅ 相同点

- 支持多级目录，路径格式相同（`/documents/2026/report.pdf`）
- 支持创建、读取、写入、删除、重命名、复制等标准文件操作
- 文件属性：名称、大小、创建时间、修改时间一应俱全
- 通过 SMB 挂载后，在 Windows 中体验与本地驱动器几乎一致

### ⚠️ 关键区别

| 对比项 | Windows NTFS | Azure File Storage |
|--------|-------------|-------------------|
| **物理存储** | 磁盘块（Block），写入磁盘扇区 | 云端对象存储，**无物理磁盘** |
| **目录实体** | 磁盘上真实的 inode/MFT 记录 | **元数据记录，不占存储空间** |
| **路径分隔符** | `\` 或 `/`（兼容） | REST API 固定 `/`，SMB 支持 `\` |
| **重命名目录** | 即时原子操作 | 需先创建新目录，再逐文件移动 |
| **事务支持** | NTFS 日志事务 | 仅单文件原子写入，不支持多文件事务 |
| **符号链接** | 支持 symlink/junction | **不支持** |
| **文件系统 API** | Win32 / .NET System.IO | Azure Storage SDK / REST API |
| **延迟** | 微秒级（本地磁盘） | 毫秒级（1-10ms，高级层 <1ms） |
| **冗余** | 依赖本地硬件 | 默认 3 副本，可升级 GRS/ZRS |

### ❓ Azure File Storage 有"物理目录"吗？

**没有传统意义上的物理目录。**

Azure File Storage 的目录（Directory）是一种"虚拟容器"——在服务端以元数据（Metadata）形式存在，记录目录名称和属性，**本身不占用存储容量配额**。

但这并不意味着目录"不真实"：
- ✅ 可以创建空目录（没有任何文件的目录），它会真实存在
- ✅ 删除目录时必须先清空（与本地一致）
- ✅ 可以对目录设置自定义元数据（键值对）
- ✅ 可以枚举目录内容

### 路径规范

| 规范项 | 要求 |
|--------|------|
| 文件名最大长度 | 255 字符 |
| 路径最大长度 | 2048 字符 |
| 最大目录嵌套深度 | 253 层 |
| 大小写敏感 | **不敏感**（与 Windows 一致） |
| 禁用字符 | `\ : * ? " < > \|` |

---

---

## 项目概述

本项目提供了一套完整的 ASP.NET Framework MVC 解决方案，用于操作 Azure Storage File Share 服务。包含：

- **Web应用**：完整的文件管理界面
- **服务层**：可复用的 Azure File Share 操作类库
- **迁移工具**：本地文件系统到 Azure 的迁移功能

### 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | ASP.NET MVC 5.2.9 |
| SDK | WindowsAzure.Storage 9.3.3 |
| 前端 | Bootstrap 3.4.1 + jQuery 3.4.1 |
| 目标框架 | .NET Framework 4.8 |

---

## 功能特性

### 文件管理
- 浏览目录结构（支持嵌套目录）
- 单文件/批量上传
- 文件下载
- 删除文件/文件夹
- 文件详情查看
- 文件搜索（递归模糊匹配）
- 生成 SAS 分享链接

### 迁移工具
- 本地到 Azure 完整迁移
- 增量同步（仅上传新/修改文件）
- 迁移进度显示
- 详细的迁移报告

---

## 快速开始

### 1. 环境要求

- Windows Server / Windows 10+
- .NET Framework 4.8 SDK
- Visual Studio 2019+ (推荐)
- Azure Storage Account

### 2. 创建 Azure Storage Account

```powershell
# 使用 Azure CLI 创建存储账户
az storage account create --name mystorageaccount --resource-group myResourceGroup --location eastus --sku Standard_LRS

# 创建文件共享
az storage share create --name myfileshare --account-name mystorageaccount
```

### 3. 获取连接密钥

在 Azure Portal -> 存储账户 -> 访问密钥，复制连接字符串。

### 4. 配置项目

编辑 `Web.config`：

```xml
<connectionStrings>
    <add name="AzureStorage" connectionString="DefaultEndpointsProtocol=https;AccountName=your_account;AccountKey=your_key;EndpointSuffix=core.windows.net" />
</connectionStrings>
```

### 5. 运行项目

```bash
# 还原 NuGet 包
nuget restore

# 构建项目
msbuild AzureFileShareDemo.csproj

# 运行
iisexpress /site:AzureFileShareDemo
```

---

## 配置说明

### Web.config 配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `StorageAccountName` | Azure 存储账户名 | - |
| `StorageAccountKey` | 存储账户密钥 | - |
| `FileShareName` | 文件共享名称 | - |
| `MaxUploadSizeMB` | 最大上传大小(MB) | 100 |
| `AllowedFileTypes` | 允许的文件类型 | *.* |

---

## API 参考

### AzureFileShareService

核心服务类，提供所有 File Share 操作。

#### 主要方法

| 方法 | 说明 | 返回值 |
|------|------|--------|
| `ListDirectoryAsync(path, maxResults, marker)` | 列出目录内容 | `FileListModel` |
| `UploadFileAsync(path, stream)` | 上传文件 | `Task` |
| `DownloadFileAsync(path, outputStream)` | 下载文件 | `Task` |
| `CreateDirectoryAsync(path)` | 创建目录 | `Task` |
| `DeleteAsync(path)` | 删除文件/目录 | `Task` |
| `MigrateFromLocalAsync(local, azure, includeSubdir)` | 完整迁移 | `MigrationResultModel` |
| `SyncDirectoryAsync(local, azure)` | 增量同步 | `MigrationResultModel` |
| `GenerateSasTokenAsync(path, hours)` | 生成SAS令牌 | `string` |

### FileItemModel

```csharp
public class FileItemModel
{
    public string Name { get; set; }              // 文件名
    public string FullPath { get; set; }         // 完整路径
    public bool IsDirectory { get; set; }        // 是否为目录
    public long Size { get; set; }               // 文件大小
    public string SizeFormatted { get; }         // 格式化大小
    public DateTime? LastModified { get; set; }  // 修改时间
}
```

---

## 代码示例

### 1. 初始化服务

```csharp
using AzureFileShareDemo.Services;

// 使用默认配置
var service = new AzureFileShareService();

// 使用连接字符串
var connectionString = "DefaultEndpointsProtocol=https;AccountName=xxx;AccountKey=xxx;";
var service = new AzureFileShareService(connectionString);
```

### 2. 列出目录内容

```csharp
// 列出根目录
var rootFiles = await service.ListDirectoryAsync("");

// 列出子目录
var subFiles = await service.ListDirectoryAsync("documents/reports", 100);
```

### 3. 上传文件

```csharp
// 上传文件流
using (var stream = File.OpenRead(@"C:\test.pdf"))
{
    await service.UploadFileAsync("documents/report.pdf", stream, fileSize);
}

// 上传本地文件
await service.UploadFileAsync("backup/data.xlsx", @"D:\Work\data.xlsx");
```

### 4. 下载文件

```csharp
// 下载到流
using (var stream = new MemoryStream())
{
    await service.DownloadFileAsync("documents/report.pdf", stream);
}

// 下载到本地文件
await service.DownloadFileAsync("documents/report.pdf", @"D:\Downloads\report.pdf");
```

### 5. 文件迁移

```csharp
// 完整迁移
var result = await service.MigrateFromLocalAsync(
    @"D:\WorkDocs",           // 本地路径
    "backup/WorkDocs",        // Azure 目标路径
    includeSubdirectories: true
);

Console.WriteLine($"上传: {result.UploadedCount}, 失败: {result.FailedCount}");
```

### 6. 增量同步

```csharp
// 仅同步新文件和修改过的文件
var syncResult = await service.SyncDirectoryAsync(@"D:\WorkDocs", "sync/WorkDocs");

Console.WriteLine($"上传: {syncResult.UploadedCount}, 跳过: {syncResult.SkippedCount}");
```

### 7. 搜索文件

```csharp
// 递归搜索文件名包含关键词的文件
var results = await service.SearchFilesAsync("report", "documents");

foreach (var file in results)
{
    Console.WriteLine($"{file.Name} - {file.FullPath}");
}
```

---

## 常见问题

### Q: 上传大文件失败？

**A:** 检查以下配置：
1. `Web.config` 中的 `maxRequestLength`
2. IIS 的 `maxAllowedContentLength`
3. Azure Storage 的文件大小限制（单文件最大 4.75 TB）

### Q: 连接失败？

**A:** 验证以下内容：
1. 存储账户名称和密钥正确
2. 网络可访问 Azure Storage 端点
3. 防火墙允许 443 端口

### Q: 支持哪些操作系统？

**A:**
- Windows Server 2012+
- Windows 10/11
- 需要 .NET Framework 4.8

---

## 项目结构

```
AzureFileShareDemo/
├── Controllers/
│   ├── FileShareController.cs    # 文件管理控制器
│   └── HomeController.cs        # 首页控制器
├── Models/
│   └── FileModels.cs             # 数据模型
├── Services/
│   └── AzureFileShareService.cs  # 核心服务
├── Views/
│   ├── FileShare/
│   │   ├── Index.cshtml          # 文件列表页
│   │   └── Migrate.cshtml        # 迁移工具页
│   └── Shared/
│       └── _Layout.cshtml        # 布局模板
├── Content/
│   └── Site.css                  # 样式文件
├── Web.config                    # 配置文件
└── packages.config               # NuGet 包
```

---

## 许可证

MIT License
