# ASP.NET MVC — 文件路径从 DB 读取，最小改动迁移到 Azure File 方案

> **核心场景**：数据库表存储文件的本地路径，代码通过路径做 `System.IO` 读取。
> 迁移目标：切换到 Azure File Storage，**DB 表结构改动最小、业务代码改动最小**。
>
> **文档版本**：v1.0 — 2026-04-27
> **配套文档**：`ASP.NET_MVC_迁移到_Azure_WebApp_完整指南.md`

---

## 目录

1. [问题分析：DB 路径字段的两种改法](#1-问题分析db-路径字段的两种改法)
2. [推荐方案：路径统一抽象层（最小改动）](#2-推荐方案路径统一抽象层最小改动)
3. [DB 表字段设计](#3-db-表字段设计)
4. [路径解析器：一个类解决新旧路径兼容](#4-路径解析器一个类解决新旧路径兼容)
5. [文件读取抽象层（IFileStorageService）](#5-文件读取抽象层ifilestorage)
6. [Controller 改造：几乎不动业务逻辑](#6-controller-改造几乎不动业务逻辑)
7. [写入新文件时如何存 Azure 路径到 DB](#7-写入新文件时如何存-azure-路径到-db)
8. [历史数据迁移策略](#8-历史数据迁移策略)
9. [本地开发与 Azure 切换](#9-本地开发与-azure-切换)
10. [完整代码汇总](#10-完整代码汇总)
11. [迁移 Checklist](#11-迁移-checklist)

---

## 1. 问题分析：DB 路径字段的两种改法

### 当前状况

```
DB 表 FileRecord:
┌────────────┬─────────────────────────────────────┐
│ Id         │ FilePath                            │
├────────────┼─────────────────────────────────────┤
│ 1001       │ D:\uploads\REQ-001\PDF\contract.pdf │
│ 1002       │ D:\uploads\REQ-001\IMG\photo.jpg    │
│ 1003       │ D:\uploads\REQ-002\EXCEL\data.xlsx  │
└────────────┴─────────────────────────────────────┘

代码:
var path = db.FileRecords.Find(id).FilePath;   // 取出路径
var bytes = File.ReadAllBytes(path);           // System.IO 读取
```

### 两种改法对比

| 方案 | DB 改动 | 代码改动 | 兼容历史数据 | 推荐度 |
|------|---------|---------|------------|--------|
| **A. 路径格式统一抽象（推荐）** | 不改字段，路径格式变化 | 加一层路径解析，业务代码几乎不动 | ✅ 自动兼容 | ⭐⭐⭐ |
| B. 新增 StorageType 字段 | 加列 `StorageType` | 读取时判断类型 | ✅ 需回填旧数据 | ⭐⭐ |
| C. 完全重写路径字段 | 更新所有行 | 改动较大 | ❌ 需全量迁移后才能切换 | ⭐ |

**结论：推荐方案 A**，仅改变路径的「格式」，加一个解析层，业务代码不感知存储类型。

---

## 2. 推荐方案：路径统一抽象层（最小改动）

### 核心思路

```
DB 中存的「路径」作为一个 URI 来对待：
  本地文件：  local://D:\uploads\REQ-001\PDF\contract.pdf
             （或保持原始 Windows 路径，自动识别）
  Azure File: azure://app-files/REQ-001/PDF/contract.pdf

代码只调用:  IFileStorageService.ReadAsync(pathFromDb)
底层自动判断 → 本地 IO  或  Azure SDK
```

**好处**：
- DB **不改结构**，只有新写入的记录路径格式不同
- 旧代码的 `File.ReadAllBytes(path)` → 替换为 `_fileService.ReadAsync(path)`，一行改动
- 新旧记录**同时可读**，迁移期间双轨并行
- 本地开发/生产环境无需额外判断

---

## 3. DB 表字段设计

### 3.1 路径格式约定

| 存储位置 | DB 中存储的路径格式 | 示例 |
|---------|-------------------|------|
| **本地文件**（历史） | Windows 绝对路径（原样保留） | `D:\uploads\REQ-001\PDF\a.pdf` |
| **Azure File**（新） | `azure://{shareName}/{azurePath}` | `azure://app-files/REQ-001/PDF/a.pdf` |
| **Azurite 本地模拟**（开发） | `azure://app-files/REQ-001/PDF/a.pdf`（同格式） | 连接字符串指向 Azurite |

### 3.2 DB 表无需改结构

```sql
-- 原有表不动，FilePath 字段继续使用
-- 只需确认字段长度足够（azure:// 路径通常比本地路径短）
-- 若原字段 VARCHAR(200) 可能不够，建议 VARCHAR(500)

-- 可选：如需明确区分来源，加一列（非必须）
ALTER TABLE FileRecord ADD StorageType NVARCHAR(10) NULL;
-- 'local' 或 'azure'，NULL 表示历史本地数据

-- 更新历史数据标记（可选，便于报表统计）
UPDATE FileRecord SET StorageType = 'local'
WHERE FilePath NOT LIKE 'azure://%';
```

### 3.3 新记录写入示例

```
-- 旧记录（保持不变）
INSERT INTO FileRecord (RequestId, FileType, FilePath)
VALUES ('REQ-001', 'PDF', 'D:\uploads\REQ-001\PDF\contract.pdf')

-- 新记录（Azure 路径）
INSERT INTO FileRecord (RequestId, FileType, FilePath)
VALUES ('REQ-002', 'PDF', 'azure://app-files/REQ-002/PDF/contract.pdf')
```

---

## 4. 路径解析器：一个类解决新旧路径兼容

```csharp
// FilePathResolver.cs
// 职责：判断路径类型，解析出必要参数

public enum StorageType
{
    Local,
    AzureFile
}

public class FilePathInfo
{
    public StorageType StorageType { get; set; }

    // 本地路径时有效
    public string LocalPath { get; set; }

    // Azure File 时有效
    public string ShareName { get; set; }    // 如 "app-files"
    public string AzurePath { get; set; }   // 如 "REQ-001/PDF/contract.pdf"
}

public static class FilePathResolver
{
    private const string AzureScheme = "azure://";

    /// <summary>
    /// 解析 DB 中存储的路径字符串
    /// 支持：
    ///   - 本地路径：D:\uploads\REQ-001\PDF\a.pdf  或  /var/uploads/...
    ///   - Azure 路径：azure://app-files/REQ-001/PDF/a.pdf
    /// </summary>
    public static FilePathInfo Parse(string pathFromDb)
    {
        if (string.IsNullOrWhiteSpace(pathFromDb))
            throw new ArgumentNullException(nameof(pathFromDb));

        if (pathFromDb.StartsWith(AzureScheme, StringComparison.OrdinalIgnoreCase))
        {
            // 格式：azure://{shareName}/{filePath}
            var withoutScheme = pathFromDb.Substring(AzureScheme.Length);
            var slashIndex = withoutScheme.IndexOf('/');

            if (slashIndex < 0)
                throw new FormatException($"Azure 路径格式错误（缺少文件路径）：{pathFromDb}");

            return new FilePathInfo
            {
                StorageType = StorageType.AzureFile,
                ShareName   = withoutScheme.Substring(0, slashIndex),
                AzurePath   = withoutScheme.Substring(slashIndex + 1)
            };
        }

        // 本地路径（Windows 或 Linux）
        return new FilePathInfo
        {
            StorageType = StorageType.Local,
            LocalPath   = pathFromDb
        };
    }

    /// <summary>
    /// 构建存入 DB 的 Azure 路径字符串
    /// </summary>
    public static string BuildAzurePath(string shareName, string azureRelativePath)
    {
        // 统一用正斜杠
        var cleanPath = azureRelativePath.Replace('\\', '/').TrimStart('/');
        return $"{AzureScheme}{shareName}/{cleanPath}";
    }

    /// <summary>
    /// 从 requestId + fileType + fileName 构建完整 Azure 路径
    /// </summary>
    public static string BuildAzurePath(string shareName,
                                        string requestId,
                                        string fileType,
                                        string fileName)
    {
        return BuildAzurePath(shareName, $"{requestId}/{fileType}/{fileName}");
    }
}
```

---

## 5. 文件读取抽象层（IFileStorageService）

这是最核心的抽象，让 Controller 不再关心文件在哪里。

```csharp
// IFileStorageService.cs — 接口定义
public interface IFileStorageService
{
    /// <summary>读取文件为字节数组</summary>
    Task<byte[]> ReadAllBytesAsync(string pathFromDb);

    /// <summary>读取文件为 Stream（大文件推荐）</summary>
    Task<Stream> OpenReadStreamAsync(string pathFromDb);

    /// <summary>写入文件，返回可存入 DB 的路径字符串</summary>
    Task<string> WriteAsync(string requestId, string fileType,
                            string fileName, Stream content);

    /// <summary>删除文件</summary>
    Task DeleteAsync(string pathFromDb);

    /// <summary>判断文件是否存在</summary>
    Task<bool> ExistsAsync(string pathFromDb);
}
```

```csharp
// FileStorageService.cs — 实现类（同时支持本地 IO 和 Azure File）
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure;
using System.IO;
using System.Threading.Tasks;

public class FileStorageService : IFileStorageService
{
    private readonly string _azureConnectionString;
    private readonly string _defaultShareName;

    public FileStorageService(string azureConnectionString, string defaultShareName)
    {
        _azureConnectionString = azureConnectionString;
        _defaultShareName      = defaultShareName;
    }

    // ─────────────────────────────────────────────
    // 读取文件
    // ─────────────────────────────────────────────

    public async Task<byte[]> ReadAllBytesAsync(string pathFromDb)
    {
        var info = FilePathResolver.Parse(pathFromDb);

        if (info.StorageType == StorageType.Local)
        {
            // 兼容历史本地路径
            return System.IO.File.ReadAllBytes(info.LocalPath);
        }

        // Azure File
        using (var stream = await OpenAzureStreamAsync(info))
        using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
    }

    public async Task<Stream> OpenReadStreamAsync(string pathFromDb)
    {
        var info = FilePathResolver.Parse(pathFromDb);

        if (info.StorageType == StorageType.Local)
        {
            return new FileStream(info.LocalPath, FileMode.Open,
                                  FileAccess.Read, FileShare.Read,
                                  bufferSize: 81920, useAsync: true);
        }

        return await OpenAzureStreamAsync(info);
    }

    // ─────────────────────────────────────────────
    // 写入文件，返回 DB 路径字符串
    // ─────────────────────────────────────────────

    public async Task<string> WriteAsync(string requestId, string fileType,
                                         string fileName, Stream content)
    {
        fileName = SanitizeFileName(fileName);

        var shareClient = GetShareClient(_defaultShareName);

        // 确保目录层级存在
        await EnsureDirectoryAsync(shareClient, requestId);
        await EnsureDirectoryAsync(shareClient, $"{requestId}/{fileType}");

        // 上传文件
        var fileClient = shareClient
            .GetDirectoryClient(requestId)
            .GetSubdirectoryClient(fileType)
            .GetFileClient(fileName);

        await fileClient.CreateAsync(content.Length > 0 ? content.Length : 0);

        // 如果 stream 不知道长度，先读到 MemoryStream
        if (!content.CanSeek)
        {
            using (var ms = new MemoryStream())
            {
                await content.CopyToAsync(ms);
                ms.Position = 0;
                await fileClient.CreateAsync(ms.Length);
                await fileClient.UploadAsync(ms);
            }
        }
        else
        {
            await fileClient.CreateAsync(content.Length);
            await fileClient.UploadAsync(content);
        }

        // 返回可存入 DB 的路径字符串
        return FilePathResolver.BuildAzurePath(_defaultShareName,
                                               requestId, fileType, fileName);
    }

    // ─────────────────────────────────────────────
    // 删除文件
    // ─────────────────────────────────────────────

    public async Task DeleteAsync(string pathFromDb)
    {
        var info = FilePathResolver.Parse(pathFromDb);

        if (info.StorageType == StorageType.Local)
        {
            if (System.IO.File.Exists(info.LocalPath))
                System.IO.File.Delete(info.LocalPath);
            return;
        }

        var fileClient = GetFileClient(info);
        await fileClient.DeleteIfExistsAsync();
    }

    // ─────────────────────────────────────────────
    // 判断文件是否存在
    // ─────────────────────────────────────────────

    public async Task<bool> ExistsAsync(string pathFromDb)
    {
        var info = FilePathResolver.Parse(pathFromDb);

        if (info.StorageType == StorageType.Local)
            return System.IO.File.Exists(info.LocalPath);

        var fileClient = GetFileClient(info);
        return await fileClient.ExistsAsync();
    }

    // ─────────────────────────────────────────────
    // 内部辅助方法
    // ─────────────────────────────────────────────

    private ShareClient GetShareClient(string shareName)
        => new ShareClient(_azureConnectionString, shareName);

    private ShareFileClient GetFileClient(FilePathInfo info)
    {
        // AzurePath 格式：{dir1}/{dir2}/.../fileName
        var parts = info.AzurePath.Split('/');
        var share = GetShareClient(info.ShareName);

        // 逐层导航到目录
        ShareDirectoryClient dir = share.GetRootDirectoryClient();
        for (int i = 0; i < parts.Length - 1; i++)
            dir = dir.GetSubdirectoryClient(parts[i]);

        return dir.GetFileClient(parts[parts.Length - 1]);
    }

    private async Task<Stream> OpenAzureStreamAsync(FilePathInfo info)
    {
        var fileClient = GetFileClient(info);
        var download = await fileClient.DownloadAsync();
        return download.Value.Content;
    }

    private async Task EnsureDirectoryAsync(ShareClient share, string dirPath)
    {
        var parts = dirPath.Split('/');
        var dir = share.GetRootDirectoryClient();
        foreach (var part in parts)
        {
            dir = dir.GetSubdirectoryClient(part);
            try { await dir.CreateAsync(); }
            catch (RequestFailedException ex)
                when (ex.ErrorCode == "ResourceAlreadyExists") { }
        }
    }

    private string SanitizeFileName(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c.ToString(), "_");
        return fileName;
    }
}
```

---

## 6. Controller 改造：几乎不动业务逻辑

### 6.1 改造前后对比

```csharp
// ══════════════════════════════════
// 改造前（直接 System.IO）
// ══════════════════════════════════
public ActionResult DownloadFile(int fileRecordId)
{
    var record = _db.FileRecords.Find(fileRecordId);
    if (record == null) return HttpNotFound();

    var bytes = File.ReadAllBytes(record.FilePath);   // ← 只改这一行
    var fileName = Path.GetFileName(record.FilePath);
    return File(bytes, "application/octet-stream", fileName);
}

// ══════════════════════════════════
// 改造后（IFileStorageService 抽象）
// ══════════════════════════════════
public async Task<ActionResult> DownloadFile(int fileRecordId)
{
    var record = _db.FileRecords.Find(fileRecordId);
    if (record == null) return HttpNotFound();

    var stream = await _fileService.OpenReadStreamAsync(record.FilePath); // ← 改这一行
    var fileName = Path.GetFileName(record.FilePath
                       .Replace('/', Path.DirectorySeparatorChar));
    return File(stream, "application/octet-stream", fileName);
}
```

**改动量**：每个下载 Action 只改 **1 行**。

### 6.2 上传改造前后

```csharp
// ══════════════════════════════════
// 改造前
// ══════════════════════════════════
public ActionResult Upload(HttpPostedFileBase file, string requestId, string fileType)
{
    var dir = Path.Combine(@"D:\uploads", requestId, fileType);
    Directory.CreateDirectory(dir);
    var savePath = Path.Combine(dir, file.FileName);
    file.SaveAs(savePath);                            // ← 本地保存

    // 存路径到 DB
    _db.FileRecords.Add(new FileRecord
    {
        RequestId = requestId,
        FileType  = fileType,
        FilePath  = savePath,                         // ← 本地路径
        UploadAt  = DateTime.Now
    });
    _db.SaveChanges();

    return Json(new { success = true });
}

// ══════════════════════════════════
// 改造后
// ══════════════════════════════════
public async Task<ActionResult> Upload(HttpPostedFileBase file,
                                       string requestId, string fileType)
{
    string savedPath;
    using (var stream = file.InputStream)
    {
        // WriteAsync 上传到 Azure，返回 "azure://app-files/REQ-001/PDF/a.pdf"
        savedPath = await _fileService.WriteAsync(               // ← 改这里
                        requestId, fileType, file.FileName, stream);
    }

    // 存路径到 DB（格式变了，但字段不变）
    _db.FileRecords.Add(new FileRecord
    {
        RequestId = requestId,
        FileType  = fileType,
        FilePath  = savedPath,                        // ← 存 azure:// 路径
        UploadAt  = DateTime.Now
    });
    _db.SaveChanges();

    return Json(new { success = true });
}
```

### 6.3 注册服务（Global.asax）

```csharp
// Global.asax.cs
public class MvcApplication : System.Web.HttpApplication
{
    // 简单静态单例（如无 DI 容器）
    public static IFileStorageService FileStorage { get; private set; }

    protected void Application_Start()
    {
        AreaRegistration.RegisterAllAreas();
        RouteConfig.RegisterRoutes(RouteTable.Routes);
        // ...

        // 初始化文件服务（从配置读取）
        FileStorage = new FileStorageService(
            azureConnectionString : ConfigHelper.StorageConnectionString,
            defaultShareName      : ConfigHelper.StorageShareName
        );
    }
}

// Controller 中使用
public class FileController : Controller
{
    private readonly IFileStorageService _fileService
        = MvcApplication.FileStorage;

    // ... Actions
}
```

> 如果项目有 Unity / Autofac DI 容器，直接注册为单例即可。

---

## 7. 写入新文件时如何存 Azure 路径到 DB

### 7.1 路径格式说明

```
azure://app-files/REQ-20260427-001/PDF/contract.pdf
         ↑           ↑                ↑
     Share名称    requestId/type    文件名
```

`FileStorageService.WriteAsync()` 会自动返回这个格式的字符串，直接存入 DB 的 `FilePath` 字段即可。

### 7.2 路径长度估算

```
azure://app-files/ = 17 字符
requestId         = 约 20 字符（如 REQ-20260427-001）
/type/            = 约 6 字符
fileName          = 约 30 字符
─────────────────────────────
合计              ≈ 73 字符（远小于 VARCHAR(200)）
```

### 7.3 DB 字段长度确认 SQL

```sql
-- 确认现有字段长度是否足够
SELECT COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'FileRecord'
  AND COLUMN_NAME = 'FilePath';

-- 如果长度 < 500，建议扩展
ALTER TABLE FileRecord ALTER COLUMN FilePath NVARCHAR(500) NOT NULL;
```

---

## 8. 历史数据迁移策略

### 8.1 迁移阶段划分

```
阶段一（当前）：
  DB 中：全部为本地路径
  代码：FileStorageService 自动走本地 IO 分支

阶段二（迁移中）：
  DB 中：旧记录本地路径 + 新记录 azure:// 路径
  代码：FileStorageService 自动判断，两种都能读

阶段三（迁移完成）：
  DB 中：全部为 azure:// 路径
  代码：FileStorageService 本地分支已不会触发（可保留或移除）
```

### 8.2 历史文件批量迁移 + 更新 DB

```csharp
// MigrationHelper.cs — 一次性迁移脚本（作为 Controller Action 或独立工具运行）
public class MigrationHelper
{
    private readonly AppDbContext _db;
    private readonly IFileStorageService _fileService;

    public MigrationHelper(AppDbContext db, IFileStorageService fileService)
    {
        _db = db;
        _fileService = fileService;
    }

    /// <summary>
    /// 迁移所有本地文件到 Azure，并更新 DB 路径
    /// 可分批执行：skip/take 控制
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(int skip = 0, int take = 100)
    {
        var result = new MigrationResult();

        // 只处理本地路径记录
        var records = _db.FileRecords
            .Where(r => !r.FilePath.StartsWith("azure://"))
            .OrderBy(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToList();

        foreach (var record in records)
        {
            try
            {
                if (!System.IO.File.Exists(record.FilePath))
                {
                    result.Skipped++;
                    result.Warnings.Add($"文件不存在，跳过：{record.FilePath}");
                    continue;
                }

                // 上传到 Azure
                string newPath;
                using (var stream = new FileStream(record.FilePath,
                    FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    newPath = await _fileService.WriteAsync(
                        record.RequestId,
                        record.FileType,
                        Path.GetFileName(record.FilePath),
                        stream);
                }

                // 更新 DB（保留旧路径备查可选）
                record.FilePath = newPath;
                // record.OldFilePath = record.FilePath; // 可选备份字段
                _db.SaveChanges();

                result.Success++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"Id={record.Id}：{ex.Message}");
            }
        }

        return result;
    }
}

public class MigrationResult
{
    public int Success { get; set; }
    public int Failed  { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors   { get; } = new List<string>();
    public List<string> Warnings { get; } = new List<string>();
}
```

```csharp
// 在 AdminController 中暴露迁移接口（加认证保护）
[Authorize(Roles = "Admin")]
public async Task<ActionResult> MigrateFiles(int skip = 0, int take = 100)
{
    var helper = new MigrationHelper(new AppDbContext(), MvcApplication.FileStorage);
    var result = await helper.MigrateAsync(skip, take);
    return Json(result, JsonRequestBehavior.AllowGet);
}
```

### 8.3 迁移验证 SQL

```sql
-- 查看迁移进度
SELECT
    COUNT(*) FILTER (WHERE FilePath NOT LIKE 'azure://%') AS 待迁移,
    COUNT(*) FILTER (WHERE FilePath     LIKE 'azure://%') AS 已迁移,
    COUNT(*)                                               AS 总计
FROM FileRecord;

-- 找出迁移失败（仍为本地路径）的记录
SELECT Id, RequestId, FileType, FilePath
FROM FileRecord
WHERE FilePath NOT LIKE 'azure://%'
ORDER BY Id;
```

---

## 9. 本地开发与 Azure 切换

### 9.1 本地 Azurite 完整配置

```bash
# 安装并启动 Azurite
npm install -g azurite
azurite --location C:\azurite-data
```

```xml
<!-- AppSettings.Local.config（不提交 Git）-->
<appSettings>
  <!-- Azurite 固定连接字符串 -->
  <add key="AzureStorageConnectionString" value="UseDevelopmentStorage=true" />
  <add key="AzureStorageShareName"        value="app-files" />
</appSettings>
```

```xml
<!-- Web.config 主文件（提交 Git）-->
<appSettings file="AppSettings.Local.config">
  <!-- 生产值由 Azure App Service 环境变量覆盖，此处留空 -->
  <add key="AzureStorageConnectionString" value="" />
  <add key="AzureStorageShareName"        value="app-files" />
</appSettings>
```

### 9.2 Azurite 初始化（创建 File Share）

```csharp
// 应用启动时确保 Share 存在（Development 环境）
public static async Task EnsureLocalShareExistsAsync(string connectionString, string shareName)
{
    var share = new ShareClient(connectionString, shareName);
    await share.CreateIfNotExistsAsync();
}

// Global.asax.cs Application_Start 中调用（仅本地开发）
if (System.Web.HttpContext.Current?.IsDebuggingEnabled == true
    || ConfigHelper.StorageConnectionString == "UseDevelopmentStorage=true")
{
    Task.Run(() => EnsureLocalShareExistsAsync(
        ConfigHelper.StorageConnectionString,
        ConfigHelper.StorageShareName)).Wait();
}
```

### 9.3 本地旧文件路径兼容说明

本地开发时，DB 中仍有旧的 `D:\uploads\...` 本地路径，`FileStorageService` 会自动走 `System.IO` 分支读取，**无需任何额外配置**。

---

## 10. 完整代码汇总

### 文件清单

```
项目根目录/
├── Services/
│   ├── IFileStorageService.cs     ← 接口定义
│   ├── FileStorageService.cs      ← 实现（本地+Azure 双路由）
│   └── FilePathResolver.cs        ← 路径解析器
├── Helpers/
│   ├── ConfigHelper.cs            ← 配置读取
│   └── MigrationHelper.cs         ← 一次性迁移工具
├── Controllers/
│   ├── FileController.cs          ← 改造后（最小改动）
│   └── AdminController.cs         ← 迁移触发接口
└── Global.asax.cs                 ← 服务注册
```

### ConfigHelper.cs

```csharp
public static class ConfigHelper
{
    public static string StorageConnectionString =>
        Environment.GetEnvironmentVariable("AzureStorageConnectionString")
        ?? ConfigurationManager.AppSettings["AzureStorageConnectionString"]
        ?? "UseDevelopmentStorage=true";  // 默认 Azurite

    public static string StorageShareName =>
        Environment.GetEnvironmentVariable("AzureStorageShareName")
        ?? ConfigurationManager.AppSettings["AzureStorageShareName"]
        ?? "app-files";
}
```

### 改造后的 FileController.cs

```csharp
public class FileController : Controller
{
    private readonly AppDbContext _db = new AppDbContext();
    private readonly IFileStorageService _fileService = MvcApplication.FileStorage;

    // ─── 下载 ───────────────────────────────
    public async Task<ActionResult> Download(int id)
    {
        var record = _db.FileRecords.Find(id);
        if (record == null) return HttpNotFound();

        // 兼容本地路径和 Azure 路径，自动判断
        var stream   = await _fileService.OpenReadStreamAsync(record.FilePath);
        var fileName = record.FilePath.Contains('/')
            ? record.FilePath.Split('/').Last()
            : Path.GetFileName(record.FilePath);

        return File(stream, "application/octet-stream", fileName);
    }

    // ─── 上传 ───────────────────────────────
    [HttpPost]
    public async Task<ActionResult> Upload(HttpPostedFileBase file,
                                           string requestId, string fileType)
    {
        if (file == null || file.ContentLength == 0)
            return Json(new { success = false, message = "文件为空" });

        string savedPath;
        using (var stream = file.InputStream)
        {
            savedPath = await _fileService.WriteAsync(
                requestId, fileType, file.FileName, stream);
        }

        _db.FileRecords.Add(new FileRecord
        {
            RequestId = requestId,
            FileType  = fileType,
            FilePath  = savedPath,      // 存 azure://app-files/... 格式
            FileName  = file.FileName,
            UploadAt  = DateTime.UtcNow
        });
        _db.SaveChanges();

        return Json(new { success = true, path = savedPath });
    }

    // ─── 删除 ───────────────────────────────
    [HttpPost]
    public async Task<ActionResult> Delete(int id)
    {
        var record = _db.FileRecords.Find(id);
        if (record == null) return HttpNotFound();

        await _fileService.DeleteAsync(record.FilePath);

        _db.FileRecords.Remove(record);
        _db.SaveChanges();

        return Json(new { success = true });
    }
}
```

---

## 11. 迁移 Checklist

### 代码改造（估计 1-2 天）

- [ ] 添加 NuGet 包：`Azure.Storage.Files.Shares`
- [ ] 创建 `FilePathResolver.cs`
- [ ] 创建 `IFileStorageService.cs` + `FileStorageService.cs`
- [ ] 创建 `ConfigHelper.cs`
- [ ] `Global.asax.cs` 注册服务单例
- [ ] 改造 Upload Action（返回值存 DB）
- [ ] 改造 Download Action（OpenReadStreamAsync）
- [ ] 改造 Delete Action（DeleteAsync）
- [ ] 本地 Azurite 测试：上传 → DB 存 azure:// 路径 → 下载成功

### DB 准备（0.5 天）

- [ ] 确认 `FilePath` 字段长度 ≥ 500
- [ ] 可选：增加 `StorageType` 列（便于统计）
- [ ] 准备迁移进度查询 SQL

### 历史数据迁移（依文件量）

- [ ] 先用 AzCopy 把文件批量上传到 Azure File Share
- [ ] 部署 `MigrationHelper`，分批执行（每批 100 条）
- [ ] 执行后 SQL 验证迁移进度
- [ ] 抽样下载验证文件完整性

### 上线

- [ ] Azure App Service 配置环境变量（连接字符串）
- [ ] 部署新代码
- [ ] 回归测试：新上传（走 Azure）+ 旧文件下载（走本地或 Azure 均可）
- [ ] 确认无异常后，下线本地服务器

---

## 附录：关键设计决策总结

| 决策点 | 选择 | 理由 |
|--------|------|------|
| DB 字段是否新增 | **不新增字段** | 只改路径格式，改动最小 |
| 路径格式 | `azure://shareName/path` | 自描述，解析简单，可扩展 |
| 新旧兼容 | **FilePathResolver 自动判断** | 迁移期间双轨并行，不影响旧数据访问 |
| 本地开发 | **Azurite 模拟器** | 离线开发，代码与生产完全一致 |
| SMB | **不使用** | 企业防火墙封 445，改用 HTTPS SDK |
| 历史迁移时机 | **在线分批迁移** | 无需停机，业务连续运行 |

---

*文档生成时间：2026-04-27 | 配套文档：`ASP.NET_MVC_迁移到_Azure_WebApp_完整指南.md`*
