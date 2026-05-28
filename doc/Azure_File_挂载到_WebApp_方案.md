# Azure File 挂载到 Web App 方案 — 代码改动最少的方式

> **核心问题**：把 Azure File Share 挂载到 Azure Web App，让应用继续用 `System.IO` 读写文件，是不是更简单？
>
> **短答案**：是的，代码改动极少，但有前提条件和限制。本文给出完整方案和适用场景判断。
>
> **文档版本**：v1.0 — 2026-04-27
> **配套文档**：`ASP.NET_MVC_迁移到_Azure_WebApp_完整指南.md`、`ASP.NET_MVC_文件路径DB适配_最小改动方案.md`

---

## 目录

1. [挂载方式 vs SDK 方式，到底哪个简单？](#1-挂载方式-vs-sdk-方式到底哪个简单)
2. [挂载方案的前提条件](#2-挂载方案的前提条件)
3. [方案架构图](#3-方案架构图)
4. [Step-by-Step：配置挂载](#4-step-by-step配置挂载)
5. [代码改动对比](#5-代码改动对比)
6. [DB 路径字段适配](#6-db-路径字段适配)
7. [本地开发怎么办](#7-本地开发怎么办)
8. [挂载方案的限制与坑](#8-挂载方案的限制与坑)
9. [两种方案选型决策表](#9-两种方案选型决策表)
10. [完整迁移 Checklist](#10-完整迁移-checklist)

---

## 1. 挂载方式 vs SDK 方式，到底哪个简单？

| 维度 | 挂载方式（SMB 映射为本地路径） | SDK 方式（REST API / HTTPS） |
|------|---------------------------|---------------------------|
| **代码改动** | ⭐⭐⭐ 几乎不改，继续 `System.IO` | ⭐⭐ 需替换 IO 调用为 SDK 调用 |
| **DB 路径字段** | 改路径前缀即可 | 需加 `azure://` 协议解析 |
| **NuGet 包** | 不需要 | 需 `Azure.Storage.Files.Shares` |
| **本地开发** | 需映射网络驱动器或用 SDK 兼容 | Azurite 一键模拟 |
| **企业网络** | Azure 内部走 SMB（445 端口在 Azure 内不受限） | HTTPS 443，内外都通 |
| **适用范围** | **仅 Azure Web App（Windows 原生代码）** | Azure / 本地 / 任意环境 |
| **性能** | 受 SMB 协议延迟影响 | SDK 有连接池，大数据量更优 |
| **可靠性** | SMB 断连偶发，App Service 会自动重连 | REST API 更稳定 |

**结论**：

- **如果应用只部署在 Azure Web App（Windows 原生代码）**，且团队希望代码改动最少 → **挂载方式更简单**
- **如果需要本地开发也用同样方式**，或者未来可能部署到其他环境 → **SDK 方式更灵活**
- **可以混合使用**：Azure 上挂载，本地开发走 SDK（通过路径判断）

---

## 2. 挂载方案的前提条件

### ✅ 必须满足

| 条件 | 说明 |
|------|------|
| Azure Web App 必须是 **Windows 原生代码**（非容器） | ASP.NET MVC Framework 属于此类 ✅ |
| Storage Account 必须是 **通用 v2** | `StorageV2`，LRS 或 ZRS 均可 |
| **App Service Plan** 必须是 **B2 及以上** | F1（免费）和 B1 不支持存储挂载 |
| **SMB 端口 445** 在 Azure 内部不受限 | Azure → Azure 内部通信无需担心企业防火墙 |
| 最多 **5 个挂载点** | 通常 1 个 File Share 足够 |

### ⚠️ 注意

| 限制 | 影响 |
|------|------|
| **不支持 FTP/FTPS 访问挂载存储** | 不能用 FTP 管理挂载目录的文件 |
| **备份不包含挂载存储** | File Share 需单独备份（Azure Backup 或快照） |
| **不支持 Azure Blob 挂载** | 只能挂 Azure Files |
| **不支持 SQLite 等文件锁场景** | SMB 锁机制弱，不要放数据库文件 |
| **只支持存储账户访问密钥认证** | 不支持 Entra ID / RBAC / Managed Identity |
| **存储防火墙需 VNet 集成** | 如果 Storage Account 开了防火墙，必须配 VNet |

---

## 3. 方案架构图

```
┌────────────────────────────────────────────────────────────────┐
│                      Azure Cloud                                │
│                                                                │
│  ┌─────────────────────────────┐                               │
│  │   Azure Web App (Windows)   │                               │
│  │   ASP.NET MVC Framework     │                               │
│  │                             │                               │
│  │   代码: System.IO.File      │                               │
│  │   ↓                         │                               │
│  │   /mounts/uploads/          │ ←── SMB 挂载点                │
│  │     ├── REQ-001/            │     (Azure 自动映射)          │
│  │     │   ├── PDF/            │                               │
│  │     │   └── IMG/            │                               │
│  │     └── REQ-002/            │                               │
│  │         └── EXCEL/          │                               │
│  └─────────┬───────────────────┘                               │
│            │ SMB (端口 445，Azure 内部，不受企业防火墙限制)       │
│            ▼                                                    │
│  ┌─────────────────────────────┐                               │
│  │   Azure File Share          │                               │
│  │   (app-files)               │                               │
│  │   Storage Account           │                               │
│  └─────────────────────────────┘                               │
└────────────────────────────────────────────────────────────────┘
```

**关键理解**：
- **Azure Web App 内部 → Azure File Share** 的 SMB 通信**走的是 Azure 内部网络**，不经过企业防火墙
- 所以之前说的「企业内部封锁 445 端口」问题，**在 Azure 内部不存在**
- 只有**本地开发机 → Azure File Share** 才会受企业防火墙影响

---

## 4. Step-by-Step：配置挂载

### Step 1：创建 Storage Account + File Share

```bash
# 创建 Storage Account
az storage account create \
  --name mystorageacct001 \
  --resource-group rg-myapp \
  --location eastasia \
  --sku Standard_LRS \
  --kind StorageV2

# 创建 File Share
az storage share create \
  --name app-files \
  --account-name mystorageacct001 \
  --quota 100
```

### Step 2：将 File Share 挂载到 Web App

#### 方式一：Azure Portal（最简单）

1. 打开 **App Service** → **Configuration** → **Path mappings**
2. 点击 **+ New Azure Storage Mount**
3. 填写：

| 字段 | 值 | 说明 |
|------|---|------|
| Name | `uploads` | 自定义标识 |
| Configuration | Basic | 无 VNet 时选 Basic |
| Storage accounts | `mystorageacct001` | 选择刚创建的 |
| Storage type | **Azure Files** | 必须选 Files |
| Share name | `app-files` | File Share 名称 |
| Access key | 自动填充 | 选择 Storage Account 后自动填 |
| Mount path | `/mounts/uploads` | ⚠️ Windows 原生代码必须是 `/mounts/xxx` 格式 |

4. 点击 **OK** → **Save**
5. 应用自动重启

#### 方式二：Azure CLI

```bash
az webapp config storage-account add \
  --resource-group rg-myapp \
  --name myapp-webapp \
  --custom-id uploads \
  --storage-type AzureFiles \
  --share-name app-files \
  --account-name mystorageacct001 \
  --access-key "your_access_key_here" \
  --mount-path /mounts/uploads
```

#### 方式三：ARM/Bicep 模板

```json
{
  "type": "Microsoft.Web/sites",
  "apiVersion": "2022-03-01",
  "name": "myapp-webapp",
  "properties": {
    "siteConfig": {
      "azureStorageAccounts": {
        "uploads": {
          "type": "AzureFiles",
          "shareName": "app-files",
          "accountName": "mystorageacct001",
          "accessKey": "your_access_key_here",
          "mountPath": "/mounts/uploads"
        }
      }
    }
  }
}
```

### Step 3：验证挂载

在 App Service 的 **Console**（Advanced Tools → Debug Console）中执行：

```cmd
dir C:\mounts\uploads
```

或：

```cmd
echo test > C:\mounts\uploads\test.txt
type C:\mounts\uploads\test.txt
del C:\mounts\uploads\test.txt
```

> ⚠️ **Windows 原生代码中，挂载路径映射到 `C:\mounts\uploads\`**（Azure 内部自动将 `/mounts/uploads` 映射到 `C:\mounts\uploads`）。

---

## 5. 代码改动对比

### 5.1 核心改动：只改配置中的基础路径

```xml
<!-- Web.config 改造前 -->
<appSettings>
  <add key="FileBasePath" value="D:\uploads\" />
</appSettings>

<!-- Web.config 改造后 -->
<appSettings>
  <add key="FileBasePath" value="C:\mounts\uploads\" />
</appSettings>
```

**业务代码（Controller / Service）完全不需要改！** 原来的 `System.IO` 代码照常工作：

```csharp
// 以下代码无需任何修改
var dir = Path.Combine(ConfigurationManager.AppSettings["FileBasePath"],
                       requestId, fileType);
Directory.CreateDirectory(dir);
var savePath = Path.Combine(dir, fileName);
file.SaveAs(savePath);                                    // ✅ 正常工作

var bytes = File.ReadAllBytes(record.FilePath);           // ✅ 正常工作
Directory.GetFiles(dir);                                  // ✅ 正常工作
File.Delete(filePath);                                    // ✅ 正常工作
```

### 5.2 DB 路径适配

#### 方案 A：只改新记录路径（最省事）

```
旧记录：D:\uploads\REQ-001\PDF\a.pdf        → 保持不动，迁移后不可读
新记录：C:\mounts\uploads\REQ-001\PDF\a.pdf  → 正常可读
```

但旧记录迁移后路径变了，需要更新 DB。

#### 方案 B：路径存相对路径（推荐）

**如果 DB 中存的是相对路径，改造成本最低：**

```csharp
// 改造前：存绝对路径
record.FilePath = Path.Combine(basePath, requestId, fileType, fileName);
// 结果：D:\uploads\REQ-001\PDF\a.pdf

// 改造后：存相对路径
record.FilePath = Path.Combine(requestId, fileType, fileName);
// 结果：REQ-001\PDF\a.pdf

// 读取时拼上基础路径
var fullPath = Path.Combine(ConfigurationManager.AppSettings["FileBasePath"],
                           record.FilePath);
var bytes = File.ReadAllBytes(fullPath);
```

**这样切换环境时只改 `FileBasePath` 配置，DB 中的路径不用动！**

#### 方案 C：迁移后批量替换 DB 路径前缀

```sql
-- 一次性替换所有记录的路径前缀
UPDATE FileRecord
SET FilePath = REPLACE(FilePath, 'D:\uploads\', 'C:\mounts\uploads\')
WHERE FilePath LIKE 'D:\uploads\%';

-- 验证
SELECT TOP 10 FilePath FROM FileRecord;
```

### 5.3 三种 DB 适配方式对比

| 方式 | DB 改动 | 代码改动 | 推荐度 |
|------|---------|---------|--------|
| **B. 存相对路径** | 需改写入逻辑，历史数据需更新 | 读取时拼前缀 | ⭐⭐⭐ 最推荐 |
| C. 批量替换前缀 | 一次 SQL 即可 | 不改 | ⭐⭐ 简单粗暴 |
| A. 只改新记录 | 最少 | 需同时支持新旧路径 | ⭐ 迁移期双轨 |

---

## 6. DB 路径字段适配（推荐方案 B：存相对路径）

### 6.1 改造上传逻辑

```csharp
// 改造前
public ActionResult Upload(HttpPostedFileBase file, string requestId, string fileType)
{
    var basePath = ConfigurationManager.AppSettings["FileBasePath"];
    var dir = Path.Combine(basePath, requestId, fileType);
    Directory.CreateDirectory(dir);
    var savePath = Path.Combine(dir, file.FileName);
    file.SaveAs(savePath);

    // 存绝对路径
    _db.FileRecords.Add(new FileRecord
    {
        RequestId = requestId,
        FileType  = fileType,
        FilePath  = savePath,    // "D:\uploads\REQ-001\PDF\a.pdf"
        UploadAt  = DateTime.Now
    });
    _db.SaveChanges();
    return Json(new { success = true });
}

// 改造后 — 只改 FilePath 存储值
public ActionResult Upload(HttpPostedFileBase file, string requestId, string fileType)
{
    var basePath = ConfigurationManager.AppSettings["FileBasePath"];
    var dir = Path.Combine(basePath, requestId, fileType);
    Directory.CreateDirectory(dir);
    var savePath = Path.Combine(dir, file.FileName);
    file.SaveAs(savePath);

    // 存相对路径（不包含 basePath）
    var relativePath = Path.Combine(requestId, fileType, file.FileName);

    _db.FileRecords.Add(new FileRecord
    {
        RequestId = requestId,
        FileType  = fileType,
        FilePath  = relativePath,  // "REQ-001\PDF\a.pdf"
        UploadAt  = DateTime.Now
    });
    _db.SaveChanges();
    return Json(new { success = true });
}
```

### 6.2 改造下载逻辑

```csharp
// 改造前
public ActionResult Download(int id)
{
    var record = _db.FileRecords.Find(id);
    var bytes = File.ReadAllBytes(record.FilePath);  // 绝对路径直接读
    return File(bytes, "application/octet-stream",
                Path.GetFileName(record.FilePath));
}

// 改造后 — 拼上 basePath
public ActionResult Download(int id)
{
    var record = _db.FileRecords.Find(id);
    var basePath = ConfigurationManager.AppSettings["FileBasePath"];
    var fullPath = Path.Combine(basePath, record.FilePath);  // 拼接
    var bytes = System.IO.File.ReadAllBytes(fullPath);
    return File(bytes, "application/octet-stream",
                Path.GetFileName(record.FilePath));
}
```

**改动量：上传改 1 行（存相对路径），下载改 1 行（拼前缀），搞定。**

### 6.3 历史数据处理

```sql
-- 把旧绝对路径转为相对路径
UPDATE FileRecord
SET FilePath = REPLACE(FilePath, 'D:\uploads\', '')
WHERE FilePath LIKE 'D:\uploads\%';

-- 验证（应全部变为相对路径）
SELECT TOP 10 FilePath FROM FileRecord;
-- 期望结果：REQ-001\PDF\a.pdf
```

---

## 7. 本地开发怎么办

这是挂载方案的**主要痛点**。Azure 上挂载简单，但本地开发机没有自动挂载。

### 7.1 三种本地开发方案

| 方案 | 操作 | 优缺点 |
|------|------|--------|
| **A. 本地仍用本地磁盘** | `FileBasePath` 指向本地 `D:\uploads\` | 最简单，开发时完全不走 Azure |
| **B. 映射网络驱动器** | `net use Z: \\xxx.file.core.windows.net\app-files` | 企业防火墙可能封 445 |
| **C. 混合模式（推荐）** | 读取时判断路径类型，挂载和 SDK 双支持 | 兼顾灵活与简单 |

### 7.2 推荐方案 A：本地用本地磁盘，Azure 用挂载路径

```xml
<!-- AppSettings.Local.config（不提交 Git）-->
<appSettings>
  <add key="FileBasePath" value="D:\uploads\" />
</appSettings>
```

```xml
<!-- Azure App Service 环境变量 -->
<!-- FileBasePath = C:\mounts\uploads\ -->
```

由于 DB 存的是**相对路径**，本地和 Azure 各自拼不同的前缀，一切正常：

```
本地读取：D:\uploads\ + REQ-001\PDF\a.pdf = D:\uploads\REQ-001\PDF\a.pdf  ✅
Azure读取：C:\mounts\uploads\ + REQ-001\PDF\a.pdf = C:\mounts\uploads\REQ-001\PDF\a.pdf ✅
```

**这是最简单的本地开发方案！**

### 7.3 本地需要访问 Azure File Share 上的文件怎么办？

如果本地开发也需要读写 Azure 上的文件（比如测试真实数据）：

#### 方式一：映射网络驱动器（需 445 端口开放）

```cmd
net use Z: \\mystorageacct001.file.core.windows.net\app-files /u:AZURE\mystorageacct001 your_access_key
```

然后 `FileBasePath` 设为 `Z:\`。

> ⚠️ 如果企业防火墙封了 445，此方式不可用。

#### 方式二：Azure Storage Explorer（可视化只读调试）

1. 下载安装 [Azure Storage Explorer](https://azure.microsoft.com/features/storage-explorer/)
2. 连接 Storage Account → File Shares → app-files
3. 浏览/下载/上传文件

#### 方式三：混合模式代码（兼顾挂载和 SDK）

```csharp
// HybridFileService.cs — 挂载优先，SDK 兜底
public class HybridFileService
{
    private readonly string _mountPath;        // 挂载路径，如 C:\mounts\uploads
    private readonly string _basePath;          // 基础路径（配置）
    private readonly AzureFileService _azure;  // SDK 服务（兜底）

    public HybridFileService(string basePath, AzureFileService azureService)
    {
        _basePath = basePath;
        _azure = azureService;
    }

    public byte[] ReadAllBytes(string relativePath)
    {
        var fullPath = Path.Combine(_basePath, relativePath);

        // 挂载路径存在 → 用 System.IO（最快）
        if (Directory.Exists(Path.GetDirectoryName(fullPath))
            && File.Exists(fullPath))
        {
            return File.ReadAllBytes(fullPath);
        }

        // 兜底 → 用 Azure SDK（REST/HTTPS）
        // 解析相对路径为 requestId / fileType / fileName
        var parts = relativePath.Split(new[] { '\\', '/' },
                              StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            return _azure.ReadAllBytesAsync(parts[0], parts[1], parts[2])
                         .GetAwaiter().GetResult();
        }

        throw new FileNotFoundException($"文件不存在：{relativePath}");
    }
}
```

---

## 8. 挂载方案的限制与坑

### 8.1 已知限制清单

| 限制 | 详情 | 影响程度 |
|------|------|---------|
| **只支持 Windows 原生代码 App** | 容器化应用需用 Windows 容器方式 | 低（MVC Framework 就是原生代码） |
| **最多 5 个挂载点** | 一个 App 最多挂 5 个 File Share | 低（通常 1 个够） |
| **SMB 断连可能偶发** | Azure 会自动重连，但期间 IO 可能失败 | 中（需加重试逻辑） |
| **不支持文件锁** | SQLite、Access 等依赖文件锁的场景不可用 | 低（文件存储不用锁） |
| **备份不包含挂载存储** | 需单独对 File Share 做备份 | 中（需额外配置快照策略） |
| **只支持访问密钥认证** | 不支持 Managed Identity / RBAC | 中（密钥需轮换管理） |
| **App Service Plan ≥ B2** | 免费/共享计划不支持 | 低（生产本就需要 B2+） |
| **VNet 集成时需配 WEBSITE_CONTENTOVERVNET=1** | 否则可能连接超时 | 低（配置一行） |

### 8.2 SMB 断连重试（推荐加上）

```csharp
// RetryHelper.cs — 简单重试，应对 SMB 偶发断连
public static class FileRetryHelper
{
    public static byte[] ReadAllBytesWithRetry(string path,
        int maxRetries = 3, int delayMs = 1000)
    {
        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException) when (i < maxRetries)
            {
                Thread.Sleep(delayMs * (i + 1));  // 递增延迟
            }
        }
        return File.ReadAllBytes(path);  // 最后一次不 catch
    }

    public static void WriteWithRetry(string path, byte[] content,
        int maxRetries = 3, int delayMs = 1000)
    {
        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                File.WriteAllBytes(path, content);
                return;
            }
            catch (IOException) when (i < maxRetries)
            {
                Thread.Sleep(delayMs * (i + 1));
            }
        }
        File.WriteAllBytes(path, content);
    }
}
```

### 8.3 File Share 备份策略

```bash
# 方式一：存储快照（Azure Portal → File Share → Snapshot）
# 快照是增量的，成本很低

# 方式二：AzCopy 定期同步到 Blob Storage（冷存储）
az storage copy \
  --source https://mystorageacct001.file.core.windows.net/app-files/?{SAS} \
  --destination https://mystorageacct001.blob.core.windows.net/backups/?{SAS} \
  --recursive
```

---

## 9. 两种方案选型决策表

| 你的情况 | 推荐方案 | 理由 |
|---------|---------|------|
| 只部署 Azure Web App，代码改动越少越好 | **挂载** | 改个配置路径就行 |
| DB 已存相对路径，或可以改为相对路径 | **挂载** | 本地/Azure 只切前缀 |
| DB 存绝对路径，不想改 DB | **SDK** | 需要路径解析层自动路由 |
| 本地开发也要连 Azure 文件 | **SDK** | Azurite 模拟，无需 445 端口 |
| 企业防火墙封 445，本地需连 Azure | **SDK** | REST/HTTPS 不受限制 |
| 多实例部署，文件需共享 | **挂载或 SDK 均可** | 都支持多实例共享 |
| 文件操作频繁、性能敏感 | **SDK** | 连接池 + 并行，性能更优 |
| 未来可能迁移到 Linux 容器 | **SDK** | 挂载方式在 Linux 容器中路径不同 |
| 团队不熟悉 Azure SDK | **挂载** | 继续用熟悉的 System.IO |

### 推荐组合策略

```
最省事的路线：

1. 先用挂载方式上线（代码改动最少，快速迁移）
2. DB 路径改为存相对路径
3. 后续如需优化（性能、灵活性），逐步引入 SDK 作为补充
4. 两种方式可以共存（挂载为主，SDK 兜底）
```

---

## 10. 完整迁移 Checklist

### 准备（0.5 天）

- [ ] 确认 App Service Plan ≥ B2
- [ ] 创建 Storage Account + File Share
- [ ] 用 AzCopy / Storage Explorer 上传历史文件到 File Share

### 配置挂载（0.5 天）

- [ ] Azure Portal 配置 File Share 挂载到 `/mounts/uploads`
- [ ] 在 App Service Console 验证 `C:\mounts\uploads` 可读写
- [ ] 设置 App Service 环境变量 `FileBasePath = C:\mounts\uploads\`

### 代码改造（0.5-1 天）

- [ ] DB 路径改为存相对路径（上传逻辑改 1 行）
- [ ] 下载逻辑加 `Path.Combine(basePath, relativePath)`（改 1 行）
- [ ] SQL 批量更新历史记录：`REPLACE(FilePath, 'D:\uploads\', '')`
- [ ] 加入 `FileRetryHelper` 处理 SMB 断连

### 本地开发配置

- [ ] `AppSettings.Local.config` 中 `FileBasePath = D:\uploads\`
- [ ] `.gitignore` 中排除 `AppSettings.Local.config`

### 测试上线

- [ ] 本地测试：上传 → DB 存相对路径 → 下载 OK
- [ ] Azure 测试：上传 → 文件写到挂载目录 → 下载 OK
- [ ] 历史文件抽样验证
- [ ] 配置 File Share 快照备份
- [ ] 上线监控 Application Insights 日志

---

*文档生成时间：2026-04-27 | 配套文档：`ASP.NET_MVC_迁移到_Azure_WebApp_完整指南.md`*
