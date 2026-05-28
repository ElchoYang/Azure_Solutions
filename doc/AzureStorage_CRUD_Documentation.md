# ASP.NET MVC Framework — Azure File Storage & Blob Storage CRUD 开发文档

> **版本**: v1.0
> **日期**: 2026-05-28
> **适用框架**: ASP.NET MVC Framework (非 Core)
> **SDK**: WindowsAzure.Storage 9.3.3 / Microsoft.Azure.Storage.Blob 11.2.3

---

## 目录

1. [概述与依赖配置](#1-概述与依赖配置)
2. [AzureFileService — Azure File Storage 操作服务](#2-azurefileservice--azure-file-storage-操作服务)
   - 2.1 [服务类定义与初始化](#21-服务类定义与初始化)
   - 2.2 [创建文件共享 (Create Share)](#22-创建文件共享-create-share)
   - 2.3 [创建目录 (Create Directory)](#23-创建目录-create-directory)
   - 2.4 [上传文件 (Upload File)](#24-上传文件-upload-file)
   - 2.5 [下载文件 (Download File)](#25-下载文件-download-file)
   - 2.6 [列出文件与目录 (List Files & Directories)](#26-列出文件与目录-list-files--directories)
   - 2.7 [删除文件 (Delete File)](#27-删除文件-delete-file)
   - 2.8 [删除目录 (Delete Directory)](#28-删除目录-delete-directory)
   - 2.9 [复制文件 (Copy File)](#29-复制文件-copy-file)
   - 2.10 [检查文件是否存在 (File Exists)](#210-检查文件是否存在-file-exists)
   - 2.11 [获取文件属性 (Get File Properties)](#211-获取文件属性-get-file-properties)
   - 2.12 [批量下载为 ZIP (Batch Download as ZIP)](#212-批量下载为-zip-batch-download-as-zip)
3. [BlobStorageService — Azure Blob Storage 操作服务](#3-blobstorageservice--azure-blob-storage-操作服务)
   - 3.1 [服务类定义与初始化](#31-服务类定义与初始化)
   - 3.2 [创建容器 (Create Container)](#32-创建容器-create-container)
   - 3.3 [创建虚拟文件夹 (Create Virtual Folder)](#33-创建虚拟文件夹-create-virtual-folder)
   - 3.4 [上传 Blob (Upload Blob)](#34-上传-blob-upload-blob)
   - 3.5 [下载 Blob (Download Blob)](#35-下载-blob-download-blob)
   - 3.6 [列出 Blob (List Blobs)](#36-列出-blob-list-blobs)
   - 3.7 [删除 Blob (Delete Blob)](#37-删除-blob-delete-blob)
   - 3.8 [删除容器 (Delete Container)](#38-删除容器-delete-container)
   - 3.9 [复制 Blob (Copy Blob)](#39-复制-blob-copy-blob)
   - 3.10 [检查 Blob 是否存在 (Blob Exists)](#310-检查-blob-是否存在-blob-exists)
   - 3.11 [获取 Blob 属性 (Get Blob Properties)](#311-获取-blob-属性-get-blob-properties)
   - 3.12 [批量下载为 ZIP (Batch Download as ZIP)](#312-批量下载为-zip-batch-download-as-zip)
4. [Controller 层调用示例](#4-controller-层调用示例)
5. [Web.config 配置](#5-webconfig-配置)
6. [NuGet 包引用](#6-nuget-包引用)
7. [常见问题与注意事项](#7-常见问题与注意事项)

---

## 1. 概述与依赖配置

本文档提供两个独立的服务类，分别封装 Azure File Storage 和 Azure Blob Storage 的完整 CRUD 操作，适用于 ASP.NET MVC Framework 项目。

| 服务类 | 职责 | 存储类型 |
|--------|------|----------|
| `AzureFileService` | Azure 文件共享操作（SMB 协议，支持目录/文件层级） | Azure File Storage |
| `BlobStorageService` | Azure Blob 存储（支持容器/虚拟文件夹/Blob） | Azure Blob Storage |

两个服务均包含 **批量打包 ZIP 下载** 功能，将多个文件打包后放到本地临时路径供用户下载。

### 核心依赖

```
WindowsAzure.Storage (>= 9.3.3)
System.IO.Compression.FileSystem
```

---

## 2. AzureFileService — Azure File Storage 操作服务

### 2.1 服务类定义与初始化

```csharp
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.File;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

public class AzureFileService
{
    private readonly CloudFileClient _fileClient;
    private readonly string _defaultShareName;

    /// <summary>
    /// 初始化 AzureFileService
    /// </summary>
    /// <param name="connectionString">Azure Storage 连接字符串</param>
    /// <param name="defaultShareName">默认文件共享名称</param>
    public AzureFileService(string connectionString, string defaultShareName = "my-file-share")
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentNullException(nameof(connectionString));

        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
        _fileClient = storageAccount.CreateCloudFileClient();
        _defaultShareName = defaultShareName;
    }

    /// <summary>
    /// 获取文件共享引用
    /// </summary>
    private CloudFileShare GetShareReference(string shareName = null)
    {
        return _fileClient.GetShareReference(shareName ?? _defaultShareName);
    }

    /// <summary>
    /// 获取根目录引用
    /// </summary>
    private CloudFileDirectory GetRootDirectory(string shareName = null)
    {
        return GetShareReference(shareName).GetRootDirectoryReference();
    }
}
```

### 2.2 创建文件共享 (Create Share)

```csharp
/// <summary>
/// 创建文件共享（如果不存在）
/// </summary>
/// <param name="shareName">文件共享名称（留空使用默认）</param>
/// <param name="quotaInGB">配额大小（GB）</param>
public async Task<bool> CreateShareAsync(string shareName = null, int quotaInGB = 5120)
{
    var share = GetShareReference(shareName);
    bool created = await share.CreateIfNotExistsAsync();
    if (created)
    {
        share.Properties.Quota = quotaInGB;
        await share.SetPropertiesAsync();
    }
    return created;
}
```

### 2.3 创建目录 (Create Directory)

```csharp
/// <summary>
/// 创建目录（支持多级路径，如 "projects/2026/reports"）
/// </summary>
/// <param name="directoryPath">目录路径，多级用 / 分隔</param>
/// <param name="shareName">文件共享名称</param>
public async Task<bool> CreateDirectoryAsync(string directoryPath, string shareName = null)
{
    if (string.IsNullOrEmpty(directoryPath))
        throw new ArgumentNullException(nameof(directoryPath));

    var rootDir = GetRootDirectory(shareName);
    var segments = directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    CloudFileDirectory currentDir = rootDir;
    bool created = false;

    foreach (var segment in segments)
    {
        currentDir = currentDir.GetDirectoryReference(segment);
        created = await currentDir.CreateIfNotExistsAsync() || created;
    }

    return created;
}
```

### 2.4 上传文件 (Upload File)

```csharp
/// <summary>
/// 上传文件到指定目录
/// </summary>
/// <param name="localFilePath">本地文件完整路径</param>
/// <param name="targetDirectoryPath">目标目录路径（如 "documents/reports"）</param>
/// <param name="shareName">文件共享名称</param>
public async Task UploadFileAsync(string localFilePath, string targetDirectoryPath = null, string shareName = null)
{
    if (!File.Exists(localFilePath))
        throw new FileNotFoundException($"本地文件不存在: {localFilePath}");

    CloudFileDirectory targetDir = GetRootDirectory(shareName);

    if (!string.IsNullOrEmpty(targetDirectoryPath))
    {
        var segments = targetDirectoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            targetDir = targetDir.GetDirectoryReference(segment);
        }
    }

    string fileName = Path.GetFileName(localFilePath);
    CloudFile cloudFile = targetDir.GetFileReference(fileName);

    await cloudFile.UploadFromFileAsync(localFilePath);
}

/// <summary>
/// 通过 Stream 上传文件
/// </summary>
/// <param name="stream">文件流</param>
/// <param name="fileName">目标文件名</param>
/// <param name="targetDirectoryPath">目标目录路径</param>
/// <param name="shareName">文件共享名称</param>
public async Task UploadFromStreamAsync(Stream stream, string fileName, string targetDirectoryPath = null, string shareName = null)
{
    if (stream == null) throw new ArgumentNullException(nameof(stream));
    if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

    CloudFileDirectory targetDir = GetRootDirectory(shareName);

    if (!string.IsNullOrEmpty(targetDirectoryPath))
    {
        var segments = targetDirectoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            targetDir = targetDir.GetDirectoryReference(segment);
        }
    }

    CloudFile cloudFile = targetDir.GetFileReference(fileName);
    await cloudFile.UploadFromStreamAsync(stream);
}
```

### 2.5 下载文件 (Download File)

```csharp
/// <summary>
/// 下载文件到本地路径
/// </summary>
/// <param name="fileName">文件名</param>
/// <param name="localFilePath">本地保存路径</param>
/// <param name="directoryPath">源目录路径</param>
/// <param name="shareName">文件共享名称</param>
public async Task DownloadFileAsync(string fileName, string localFilePath, string directoryPath = null, string shareName = null)
{
    if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

    // 确保本地目录存在
    var localDir = Path.GetDirectoryName(localFilePath);
    if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
    {
        Directory.CreateDirectory(localDir);
    }

    CloudFileDirectory targetDir = GetRootDirectory(shareName);

    if (!string.IsNullOrEmpty(directoryPath))
    {
        var segments = directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            targetDir = targetDir.GetDirectoryReference(segment);
        }
    }

    CloudFile cloudFile = targetDir.GetFileReference(fileName);
    await cloudFile.DownloadToFileAsync(localFilePath, FileMode.Create);
}

/// <summary>
/// 下载文件到 Stream
/// </summary>
public async Task DownloadToStreamAsync(string fileName, Stream targetStream, string directoryPath = null, string shareName = null)
{
    if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

    CloudFileDirectory targetDir = GetRootDirectory(shareName);

    if (!string.IsNullOrEmpty(directoryPath))
    {
        var segments = directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            targetDir = targetDir.GetDirectoryReference(segment);
        }
    }

    CloudFile cloudFile = targetDir.GetFileReference(fileName);
    await cloudFile.DownloadToStreamAsync(targetStream);
}
```

### 2.6 列出文件与目录 (List Files & Directories)

```csharp
/// <summary>
/// 列出指定目录下的所有文件和子目录
/// </summary>
/// <returns>文件列表和目录列表</returns>
public async Task<FileListResult> ListFilesAndDirectoriesAsync(string directoryPath = null, string shareName = null)
{
    CloudFileDirectory targetDir = GetRootDirectory(shareName);

    if (!string.IsNullOrEmpty(directoryPath))
    {
        var segments = directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            targetDir = targetDir.GetDirectoryReference(segment);
        }
    }

    var result = new FileListResult();
    FileContinuationToken token = null;

    do
    {
        var listingResult = await targetDir.ListFilesAndDirectoriesSegmentedAsync(token);
        token = listingResult.ContinuationToken;

        foreach (IListFileItem item in listingResult.Results)
        {
            if (item is CloudFile file)
            {
                await file.FetchAttributesAsync();
                result.Files.Add(new FileItem
                {
                    Name = file.Name,
                    Size = file.Properties.Length,
                    LastModified = file.Properties.LastModified.HasValue
                        ? file.Properties.LastModified.Value.DateTime : DateTime.MinValue,
                    Type = "File"
                });
            }
            else if (item is CloudFileDirectory dir)
            {
                result.Directories.Add(new FileItem
                {
                    Name = dir.Name,
                    Type = "Directory"
                });
            }
        }
    } while (token != null);

    return result;
}

/// <summary>
/// 文件列表结果模型
/// </summary>
public class FileListResult
{
    public List<FileItem> Files { get; set; } = new List<FileItem>();
    public List<FileItem> Directories { get; set; } = new List<FileItem>();
}

/// <summary>
/// 文件/目录信息模型
/// </summary>
public class FileItem
{
    public string Name { get; set; }
    public string Type { get; set; }   // "File" 或 "Directory"
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}
```

### 2.7 删除文件 (Delete File)

```csharp
/// <summary>
/// 删除指定文件
/// </summary>
/// <param name="fileName">文件名</param>
/// <param name="directoryPath">目录路径</param>
/// <param name="shareName">文件共享名称</param>
public async Task<bool> DeleteFileAsync(string fileName, string directoryPath = null, string shareName = null)
{
    if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

    CloudFileDirectory targetDir = GetRootDirectory(shareName);

    if (!string.IsNullOrEmpty(directoryPath))
    {
        var segments = directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            targetDir = targetDir.GetDirectoryReference(segment);
        }
    }

    CloudFile cloudFile = targetDir.GetFileReference(fileName);
    return await cloudFile.DeleteIfExistsAsync();
}
```

### 2.8 删除目录 (Delete Directory)

```csharp
/// <summary>
/// 删除指定目录（递归删除目录下所有文件和子目录）
/// </summary>
/// <param name="directoryPath">目录路径</param>
/// <param name="shareName">文件共享名称</param>
public async Task<bool> DeleteDirectoryAsync(string directoryPath, string shareName = null)
{
    if (string.IsNullOrEmpty(directoryPath))
        throw new ArgumentNullException(nameof(directoryPath));

    var rootDir = GetRootDirectory(shareName);
    var segments = directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    CloudFileDirectory targetDir = rootDir;
    foreach (var segment in segments)
    {
        targetDir = targetDir.GetDirectoryReference(segment);
    }

    // 先递归删除所有子文件
    var items = await ListFilesAndDirectoriesAsync(directoryPath, shareName);
    foreach (var file in items.Files)
    {
        await DeleteFileAsync(file.Name, directoryPath, shareName);
    }
    foreach (var dir in items.Directories)
    {
        string subPath = directoryPath.TrimEnd('/') + "/" + dir.Name;
        await DeleteDirectoryAsync(subPath, shareName);
    }

    return await targetDir.DeleteIfExistsAsync();
}
```

### 2.9 复制文件 (Copy File)

```csharp
/// <summary>
/// 复制文件到同共享内的另一个位置
/// </summary>
/// <param name="sourceFileName">源文件名</param>
/// <param name="sourceDirectoryPath">源目录路径</param>
/// <param name="destFileName">目标文件名</param>
/// <param name="destDirectoryPath">目标目录路径</param>
/// <param name="shareName">文件共享名称</param>
public async Task CopyFileAsync(
    string sourceFileName,
    string sourceDirectoryPath,
    string destFileName,
    string destDirectoryPath,
    string shareName = null)
{
    if (string.IsNullOrEmpty(sourceFileName))
        throw new ArgumentNullException(nameof(sourceFileName));
    if (string.IsNullOrEmpty(destFileName))
        throw new ArgumentNullException(nameof(destFileName));

    // 获取源文件引用
    CloudFileDirectory sourceDir = GetRootDirectory(shareName);
    if (!string.IsNullOrEmpty(sourceDirectoryPath))
    {
        foreach (var seg in sourceDirectoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            sourceDir = sourceDir.GetDirectoryReference(seg);
    }
    var sourceFile = sourceDir.GetFileReference(sourceFileName);
    await sourceFile.FetchAttributesAsync(); // 确保存在

    // 确保目标目录存在
    if (!string.IsNullOrEmpty(destDirectoryPath))
    {
        await CreateDirectoryAsync(destDirectoryPath, shareName);
    }

    // 获取目标文件引用并执行复制
    CloudFileDirectory destDir = GetRootDirectory(shareName);
    if (!string.IsNullOrEmpty(destDirectoryPath))
    {
        foreach (var seg in destDirectoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            destDir = destDir.GetDirectoryReference(seg);
    }
    var destFile = destDir.GetFileReference(destFileName);

    string sourceUri = sourceFile.SnapshotQualifiedUri.AbsoluteUri;
    await destFile.StartCopyAsync(new Uri(sourceUri));
}
```

### 2.10 检查文件是否存在 (File Exists)

```csharp
/// <summary>
/// 检查文件是否存在
/// </summary>
public async Task<bool> FileExistsAsync(string fileName, string directoryPath = null, string shareName = null)
{
    if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

    CloudFileDirectory targetDir = GetRootDirectory(shareName);
    if (!string.IsNullOrEmpty(directoryPath))
    {
        foreach (var seg in directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            targetDir = targetDir.GetDirectoryReference(seg);
    }

    CloudFile cloudFile = targetDir.GetFileReference(fileName);
    return await cloudFile.ExistsAsync();
}
```

### 2.11 获取文件属性 (Get File Properties)

```csharp
/// <summary>
/// 获取文件属性信息
/// </summary>
public async Task<FileProperties> GetFilePropertiesAsync(string fileName, string directoryPath = null, string shareName = null)
{
    if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

    CloudFileDirectory targetDir = GetRootDirectory(shareName);
    if (!string.IsNullOrEmpty(directoryPath))
    {
        foreach (var seg in directoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            targetDir = targetDir.GetDirectoryReference(seg);
    }

    CloudFile cloudFile = targetDir.GetFileReference(fileName);
    await cloudFile.FetchAttributesAsync();

    return new FileProperties
    {
        Name = cloudFile.Name,
        Length = cloudFile.Properties.Length,
        ContentType = cloudFile.Properties.ContentType,
        LastModified = cloudFile.Properties.LastModified?.DateTime ?? DateTime.MinValue,
        ETag = cloudFile.Properties.ETag,
        Metadata = cloudFile.Metadata
    };
}

/// <summary>
/// 文件属性模型
/// </summary>
public class FileProperties
{
    public string Name { get; set; }
    public long Length { get; set; }
    public string ContentType { get; set; }
    public DateTime LastModified { get; set; }
    public string ETag { get; set; }
    public IDictionary<string, string> Metadata { get; set; }
}
```

### 2.12 批量下载为 ZIP (Batch Download as ZIP)

```csharp
/// <summary>
/// 将指定目录下的多个文件打包为 ZIP 并保存到本地路径
/// </summary>
/// <param name="directoryPath">Azure File 中的目录路径</param>
/// <param name="fileNames">要打包的文件名列表（null 或空则打包整个目录）</param>
/// <param name="localZipPath">本地 ZIP 文件保存路径</param>
/// <param name="shareName">文件共享名称</param>
public async Task DownloadAsZipAsync(
    string directoryPath,
    List<string> fileNames = null,
    string localZipPath = null,
    string shareName = null)
{
    // 默认 ZIP 路径
    if (string.IsNullOrEmpty(localZipPath))
    {
        string zipDir = HttpContext.Current.Server.MapPath("~/App_Data/Downloads");
        if (!Directory.Exists(zipDir)) Directory.CreateDirectory(zipDir);

        string dirSafeName = string.IsNullOrEmpty(directoryPath)
            ? "root"
            : directoryPath.Replace("/", "_").Replace("\\", "_");
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        localZipPath = Path.Combine(zipDir, $"{dirSafeName}_{timestamp}.zip");
    }

    // 确保 ZIP 文件的父目录存在
    var zipParentDir = Path.GetDirectoryName(localZipPath);
    if (!string.IsNullOrEmpty(zipParentDir) && !Directory.Exists(zipParentDir))
    {
        Directory.CreateDirectory(zipParentDir);
    }

    // 获取文件列表
    var allItems = await ListFilesAndDirectoriesAsync(directoryPath, shareName);
    IEnumerable<FileItem> filesToDownload = allItems.Files;

    if (fileNames != null && fileNames.Any())
    {
        filesToDownload = filesToDownload.Where(f => fileNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase));
    }

    if (!filesToDownload.Any())
        throw new InvalidOperationException("没有找到可下载的文件。");

    // 创建 ZIP 文件
    using (var zipStream = new FileStream(localZipPath, FileMode.Create))
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
    {
        foreach (var fileItem in filesToDownload)
        {
            // 下载到内存流再写入 ZIP
            using (var memoryStream = new MemoryStream())
            {
                await DownloadToStreamAsync(fileItem.Name, memoryStream, directoryPath, shareName);
                memoryStream.Position = 0;

                var zipEntry = archive.CreateEntry(fileItem.Name, CompressionLevel.Optimal);

                using (var entryStream = zipEntry.Open())
                {
                    await memoryStream.CopyToAsync(entryStream);
                }
            }
        }
    }
}

/// <summary>
/// 批量下载为 ZIP 并返回文件流（用于 Controller 直接返回给浏览器）
/// </summary>
public async Task<Stream> DownloadAsZipStreamAsync(
    string directoryPath,
    List<string> fileNames = null,
    string zipFileName = null,
    string shareName = null)
{
    string dirSafeName = string.IsNullOrEmpty(directoryPath)
        ? "root"
        : directoryPath.Replace("/", "_").Replace("\\", "_");
    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    string actualZipName = string.IsNullOrEmpty(zipFileName)
        ? $"{dirSafeName}_{timestamp}.zip"
        : zipFileName;

    var allItems = await ListFilesAndDirectoriesAsync(directoryPath, shareName);
    IEnumerable<FileItem> filesToDownload = allItems.Files;

    if (fileNames != null && fileNames.Any())
    {
        filesToDownload = filesToDownload.Where(f => fileNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase));
    }

    if (!filesToDownload.Any())
        throw new InvalidOperationException("没有找到可下载的文件。");

    var memoryStream = new MemoryStream();
    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var fileItem in filesToDownload)
        {
            using (var fileMemoryStream = new MemoryStream())
            {
                await DownloadToStreamAsync(fileItem.Name, fileMemoryStream, directoryPath, shareName);
                fileMemoryStream.Position = 0;

                var zipEntry = archive.CreateEntry(fileItem.Name, CompressionLevel.Optimal);
                using (var entryStream = zipEntry.Open())
                {
                    await fileMemoryStream.CopyToAsync(entryStream);
                }
            }
        }
    }

    memoryStream.Position = 0;
    return memoryStream;
}
```

---

## 3. BlobStorageService — Azure Blob Storage 操作服务

### 3.1 服务类定义与初始化

```csharp
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

public class BlobStorageService
{
    private readonly CloudBlobClient _blobClient;
    private readonly string _defaultContainerName;

    /// <summary>
    /// 初始化 BlobStorageService
    /// </summary>
    /// <param name="connectionString">Azure Storage 连接字符串</param>
    /// <param name="defaultContainerName">默认容器名称</param>
    public BlobStorageService(string connectionString, string defaultContainerName = "my-blob-container")
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentNullException(nameof(connectionString));

        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
        _blobClient = storageAccount.CreateCloudBlobClient();
        _defaultContainerName = defaultContainerName;
    }

    /// <summary>
    /// 获取容器引用
    /// </summary>
    private CloudBlobContainer GetContainerReference(string containerName = null)
    {
        return _blobClient.GetContainerReference(containerName ?? _defaultContainerName);
    }

    /// <summary>
    /// 获取 Blob 引用
    /// </summary>
    private CloudBlockBlob GetBlobReference(string blobName, string containerName = null, string prefix = null)
    {
        var container = GetContainerReference(containerName);
        string fullBlobName = string.IsNullOrEmpty(prefix)
            ? blobName
            : prefix.TrimEnd('/') + "/" + blobName.TrimStart('/');
        return container.GetBlockBlobReference(fullBlobName);
    }
}
```

### 3.2 创建容器 (Create Container)

```csharp
/// <summary>
/// 创建容器（如果不存在）
/// </summary>
/// <param name="containerName">容器名称（留空使用默认）</param>
/// <param name="publicAccess">公开访问级别（默认私有）</param>
public async Task<bool> CreateContainerAsync(
    string containerName = null,
    BlobContainerPublicAccessType publicAccess = BlobContainerPublicAccessType.Off)
{
    var container = GetContainerReference(containerName);
    bool created = await container.CreateIfNotExistsAsync(publicAccess, new BlobRequestOptions(), new OperationContext());
    return created;
}
```

### 3.3 创建虚拟文件夹 (Create Virtual Folder)

> **注意**：Azure Blob Storage 没有真正的"文件夹"概念。文件夹是通过在 Blob 名称中使用 `/` 分隔符模拟的。创建"文件夹"实际上是在指定路径下上传一个占位 Blob（长度为 0 的 Blob，名称以 `/` 结尾）。

```csharp
/// <summary>
/// 创建虚拟文件夹（通过占位 Blob 实现）
/// </summary>
/// <param name="folderPath">文件夹路径（如 "documents/reports"）</param>
/// <param name="containerName">容器名称</param>
public async Task CreateVirtualFolderAsync(string folderPath, string containerName = null)
{
    if (string.IsNullOrEmpty(folderPath))
        throw new ArgumentNullException(nameof(folderPath));

    var container = GetContainerReference(containerName);
    string placeholderBlobName = folderPath.TrimEnd('/') + "/.folder";

    CloudBlockBlob placeholderBlob = container.GetBlockBlobReference(placeholderBlobName);
    await placeholderBlob.UploadFromByteArrayAsync(new byte[0], 0, 0);
}
```

### 3.4 上传 Blob (Upload Blob)

```csharp
/// <summary>
/// 上传本地文件到 Blob Storage
/// </summary>
/// <param name="localFilePath">本地文件路径</param>
/// <param name="blobName">Blob 名称（可含路径，如 "docs/report.pdf"）</param>
/// <param name="containerName">容器名称</param>
/// <param name="contentType">MIME 类型（可选，自动检测）</param>
public async Task UploadBlobAsync(string localFilePath, string blobName, string containerName = null, string contentType = null)
{
    if (!File.Exists(localFilePath))
        throw new FileNotFoundException($"本地文件不存在: {localFilePath}");
    if (string.IsNullOrEmpty(blobName))
        throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);

    if (!string.IsNullOrEmpty(contentType))
    {
        blob.Properties.ContentType = contentType;
    }

    await blob.UploadFromFileAsync(localFilePath);
}

/// <summary>
/// 通过 Stream 上传 Blob
/// </summary>
/// <param name="stream">文件流</param>
/// <param name="blobName">Blob 名称</param>
/// <param name="containerName">容器名称</param>
/// <param name="contentType">MIME 类型</param>
public async Task UploadFromStreamAsync(Stream stream, string blobName, string containerName = null, string contentType = null)
{
    if (stream == null) throw new ArgumentNullException(nameof(stream));
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);

    if (!string.IsNullOrEmpty(contentType))
    {
        blob.Properties.ContentType = contentType;
    }

    await blob.UploadFromStreamAsync(stream);
}

/// <summary>
/// 上传字节数组到 Blob
/// </summary>
/// <param name="data">字节数据</param>
/// <param name="blobName">Blob 名称</param>
/// <param name="containerName">容器名称</param>
/// <param name="contentType">MIME 类型</param>
public async Task UploadFromByteArrayAsync(byte[] data, string blobName, string containerName = null, string contentType = null)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);

    if (!string.IsNullOrEmpty(contentType))
    {
        blob.Properties.ContentType = contentType;
    }

    await blob.UploadFromByteArrayAsync(data, 0, data.Length);
}
```

### 3.5 下载 Blob (Download Blob)

```csharp
/// <summary>
/// 下载 Blob 到本地文件
/// </summary>
/// <param name="blobName">Blob 名称</param>
/// <param name="localFilePath">本地保存路径</param>
/// <param name="containerName">容器名称</param>
public async Task DownloadBlobAsync(string blobName, string localFilePath, string containerName = null)
{
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var localDir = Path.GetDirectoryName(localFilePath);
    if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
    {
        Directory.CreateDirectory(localDir);
    }

    var blob = GetBlobReference(blobName, containerName);
    await blob.DownloadToFileAsync(localFilePath, FileMode.Create);
}

/// <summary>
/// 下载 Blob 到 Stream
/// </summary>
public async Task DownloadToStreamAsync(string blobName, Stream targetStream, string containerName = null)
{
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);
    await blob.DownloadToStreamAsync(targetStream);
}

/// <summary>
/// 下载 Blob 为字节数组
/// </summary>
public async Task<byte[]> DownloadAsByteArrayAsync(string blobName, string containerName = null)
{
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);
    await blob.FetchAttributesAsync();

    long fileByteLength = blob.Properties.Length;
    byte[] fileBytes = new byte[fileByteLength];

    await blob.DownloadToByteArrayAsync(fileBytes, 0);
    return fileBytes;
}
```

### 3.6 列出 Blob (List Blobs)

```csharp
/// <summary>
/// 列出容器中的 Blob（可按前缀/虚拟文件夹筛选）
/// </summary>
/// <param name="prefix">路径前缀（如 "documents/" 列出 documents 下的所有 Blob）</param>
/// <param name="containerName">容器名称</param>
/// <returns>Blob 列表</returns>
public async Task<List<BlobItem>> ListBlobsAsync(string prefix = null, string containerName = null)
{
    var container = GetContainerReference(containerName);
    var result = new List<BlobItem>();
    BlobContinuationToken token = null;

    do
    {
        BlobResultSegment segment = await container.ListBlobsSegmentedAsync(
            prefix,
            useFlatBlobListing: true,  // 扁平列出（包含路径中的 /）
            blobListingDetails: BlobListingDetails.Metadata,
            maxResults: null,
            currentToken: token,
            options: null,
            operationContext: null);

        token = segment.ContinuationToken;

        foreach (IListBlobItem item in segment.Results)
        {
            if (item is CloudBlockBlob blob)
            {
                result.Add(new BlobItem
                {
                    Name = blob.Name,
                    Size = blob.Properties.Length,
                    LastModified = blob.Properties.LastModified?.DateTime ?? DateTime.MinValue,
                    ContentType = blob.Properties.ContentType,
                    Type = "Blob"
                });
            }
            else if (item is CloudBlobDirectory dir)
            {
                result.Add(new BlobItem
                {
                    Name = dir.Prefix,
                    Type = "VirtualDirectory"
                });
            }
        }
    } while (token != null);

    return result;
}

/// <summary>
/// Blob 信息模型
/// </summary>
public class BlobItem
{
    public string Name { get; set; }
    public string Type { get; set; }   // "Blob" 或 "VirtualDirectory"
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; }
}
```

### 3.7 删除 Blob (Delete Blob)

```csharp
/// <summary>
/// 删除单个 Blob
/// </summary>
/// <param name="blobName">Blob 名称</param>
/// <param name="containerName">容器名称</param>
public async Task<bool> DeleteBlobAsync(string blobName, string containerName = null)
{
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);
    return await blob.DeleteIfExistsAsync();
}

/// <summary>
/// 批量删除 Blob（按前缀）
/// </summary>
/// <param name="prefix">Blob 名称前缀</param>
/// <param name="containerName">容器名称</param>
/// <returns>删除的 Blob 数量</returns>
public async Task<int> DeleteBlobsByPrefixAsync(string prefix, string containerName = null)
{
    if (string.IsNullOrEmpty(prefix)) throw new ArgumentNullException(nameof(prefix));

    var blobs = await ListBlobsAsync(prefix, containerName);
    int count = 0;

    foreach (var blob in blobs.Where(b => b.Type == "Blob"))
    {
        if (await DeleteBlobAsync(blob.Name, containerName))
            count++;
    }

    return count;
}
```

### 3.8 删除容器 (Delete Container)

```csharp
/// <summary>
/// 删除容器
/// </summary>
/// <param name="containerName">容器名称</param>
public async Task<bool> DeleteContainerAsync(string containerName = null)
{
    var container = GetContainerReference(containerName);
    return await container.DeleteIfExistsAsync();
}
```

### 3.9 复制 Blob (Copy Blob)

```csharp
/// <summary>
/// 复制 Blob（支持跨容器）
/// </summary>
/// <param name="sourceBlobName">源 Blob 名称</param>
/// <param name="destBlobName">目标 Blob 名称</param>
/// <param name="sourceContainerName">源容器名称</param>
/// <param name="destContainerName">目标容器名称</param>
public async Task CopyBlobAsync(
    string sourceBlobName,
    string destBlobName,
    string sourceContainerName = null,
    string destContainerName = null)
{
    if (string.IsNullOrEmpty(sourceBlobName)) throw new ArgumentNullException(nameof(sourceBlobName));
    if (string.IsNullOrEmpty(destBlobName)) throw new ArgumentNullException(nameof(destBlobName));

    var sourceContainer = GetContainerReference(sourceContainerName);
    var sourceBlob = sourceContainer.GetBlockBlobReference(sourceBlobName);
    await sourceBlob.FetchAttributesAsync();

    var destBlob = GetBlobReference(destBlobName, destContainerName);
    string sourceUri = sourceBlob.SnapshotQualifiedStorageUri.PrimaryUri.AbsoluteUri;

    await destBlob.StartCopyAsync(new Uri(sourceUri));
}
```

### 3.10 检查 Blob 是否存在 (Blob Exists)

```csharp
/// <summary>
/// 检查 Blob 是否存在
/// </summary>
public async Task<bool> BlobExistsAsync(string blobName, string containerName = null)
{
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);
    return await blob.ExistsAsync();
}
```

### 3.11 获取 Blob 属性 (Get Blob Properties)

```csharp
/// <summary>
/// 获取 Blob 属性信息
/// </summary>
public async Task<BlobPropertiesInfo> GetBlobPropertiesAsync(string blobName, string containerName = null)
{
    if (string.IsNullOrEmpty(blobName)) throw new ArgumentNullException(nameof(blobName));

    var blob = GetBlobReference(blobName, containerName);
    await blob.FetchAttributesAsync();

    return new BlobPropertiesInfo
    {
        Name = blob.Name,
        Size = blob.Properties.Length,
        ContentType = blob.Properties.ContentType,
        LastModified = blob.Properties.LastModified?.DateTime ?? DateTime.MinValue,
        ETag = blob.Properties.ETag,
        BlobType = blob.Properties.BlobType.ToString(),
        LeaseStatus = blob.Properties.LeaseStatus.ToString(),
        Metadata = blob.Metadata
    };
}

/// <summary>
/// Blob 属性模型
/// </summary>
public class BlobPropertiesInfo
{
    public string Name { get; set; }
    public long Size { get; set; }
    public string ContentType { get; set; }
    public DateTime LastModified { get; set; }
    public string ETag { get; set; }
    public string BlobType { get; set; }
    public string LeaseStatus { get; set; }
    public IDictionary<string, string> Metadata { get; set; }
}
```

### 3.12 批量下载为 ZIP (Batch Download as ZIP)

```csharp
/// <summary>
/// 将指定前缀下的多个 Blob 打包为 ZIP 并保存到本地路径
/// </summary>
/// <param name="prefix">Blob 名称前缀（虚拟文件夹路径）</param>
/// <param name="blobNames">要打包的 Blob 名称列表（null 或空则打包整个前缀下所有 Blob）</param>
/// <param name="localZipPath">本地 ZIP 文件保存路径</param>
/// <param name="containerName">容器名称</param>
public async Task DownloadAsZipAsync(
    string prefix = null,
    List<string> blobNames = null,
    string localZipPath = null,
    string containerName = null)
{
    // 默认 ZIP 路径
    if (string.IsNullOrEmpty(localZipPath))
    {
        string zipDir = HttpContext.Current.Server.MapPath("~/App_Data/Downloads");
        if (!Directory.Exists(zipDir)) Directory.CreateDirectory(zipDir);

        string prefixSafeName = string.IsNullOrEmpty(prefix)
            ? "blobs"
            : prefix.Replace("/", "_").Replace("\\", "_");
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        localZipPath = Path.Combine(zipDir, $"{prefixSafeName}_{timestamp}.zip");
    }

    // 确保 ZIP 文件的父目录存在
    var zipParentDir = Path.GetDirectoryName(localZipPath);
    if (!string.IsNullOrEmpty(zipParentDir) && !Directory.Exists(zipParentDir))
    {
        Directory.CreateDirectory(zipParentDir);
    }

    // 获取 Blob 列表
    var allBlobs = await ListBlobsAsync(prefix, containerName);
    IEnumerable<BlobItem> blobsToDownload = allBlobs.Where(b => b.Type == "Blob");

    if (blobNames != null && blobNames.Any())
    {
        blobsToDownload = blobsToDownload.Where(b =>
            blobNames.Contains(Path.GetFileName(b.Name), StringComparer.OrdinalIgnoreCase));
    }

    if (!blobsToDownload.Any())
        throw new InvalidOperationException("没有找到可下载的 Blob。");

    // 创建 ZIP 文件
    using (var zipStream = new FileStream(localZipPath, FileMode.Create))
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
    {
        foreach (var blobItem in blobsToDownload)
        {
            using (var memoryStream = new MemoryStream())
            {
                await DownloadToStreamAsync(blobItem.Name, memoryStream, containerName);
                memoryStream.Position = 0;

                // 使用 Blob 的文件名（不含路径前缀）作为 ZIP 内条目名
                string entryName = Path.GetFileName(blobItem.Name);
                var zipEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                using (var entryStream = zipEntry.Open())
                {
                    await memoryStream.CopyToAsync(entryStream);
                }
            }
        }
    }
}

/// <summary>
/// 批量下载为 ZIP 并返回内存流（用于 Controller 直接返回给浏览器）
/// </summary>
public async Task<Stream> DownloadAsZipStreamAsync(
    string prefix = null,
    List<string> blobNames = null,
    string zipFileName = null,
    string containerName = null)
{
    string prefixSafeName = string.IsNullOrEmpty(prefix)
        ? "blobs"
        : prefix.Replace("/", "_").Replace("\\", "_");
    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    string actualZipName = string.IsNullOrEmpty(zipFileName)
        ? $"{prefixSafeName}_{timestamp}.zip"
        : zipFileName;

    var allBlobs = await ListBlobsAsync(prefix, containerName);
    IEnumerable<BlobItem> blobsToDownload = allBlobs.Where(b => b.Type == "Blob");

    if (blobNames != null && blobNames.Any())
    {
        blobsToDownload = blobsToDownload.Where(b =>
            blobNames.Contains(Path.GetFileName(b.Name), StringComparer.OrdinalIgnoreCase));
    }

    if (!blobsToDownload.Any())
        throw new InvalidOperationException("没有找到可下载的 Blob。");

    var memoryStream = new MemoryStream();
    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var blobItem in blobsToDownload)
        {
            using (var fileMemoryStream = new MemoryStream())
            {
                await DownloadToStreamAsync(blobItem.Name, fileMemoryStream, containerName);
                fileMemoryStream.Position = 0;

                string entryName = Path.GetFileName(blobItem.Name);
                var zipEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using (var entryStream = zipEntry.Open())
                {
                    await fileMemoryStream.CopyToAsync(entryStream);
                }
            }
        }
    }

    memoryStream.Position = 0;
    return memoryStream;
}
```

---

## 4. Controller 层调用示例

### 4.1 基础 Controller 配置

```csharp
using System.Web.Mvc;

public class AzureStorageController : Controller
{
    private readonly AzureFileService _fileService;
    private readonly BlobStorageService _blobService;

    public AzureStorageController()
    {
        string connectionString = System.Configuration.ConfigurationManager
            .ConnectionStrings["AzureStorageConnection"].ConnectionString;

        _fileService = new AzureFileService(connectionString, "my-file-share");
        _blobService = new BlobStorageService(connectionString, "my-blob-container");
    }
}
```

### 4.2 Azure File Storage Controller 示例

```csharp
// ========== 文件共享 & 目录 ==========

public async Task<ActionResult> CreateShare()
{
    await _fileService.CreateShareAsync("project-files", 1024);
    return Json(new { success = true, message = "文件共享创建成功" }, JsonRequestBehavior.AllowGet);
}

public async Task<ActionResult> CreateDirectory(string path)
{
    await _fileService.CreateDirectoryAsync(path);
    return Json(new { success = true, message = $"目录 {path} 创建成功" }, JsonRequestBehavior.AllowGet);
}

// ========== 上传文件 ==========

public async Task<ActionResult> UploadFile()
{
    if (Request.Files.Count == 0)
        return Json(new { success = false, message = "未选择文件" });

    var file = Request.Files[0];
    string directoryPath = Request.Form["directoryPath"];
    string shareName = Request.Form["shareName"];

    using (var stream = file.InputStream)
    {
        await _fileService.UploadFromStreamAsync(stream, file.FileName, directoryPath, shareName);
    }

    return Json(new { success = true, message = $"文件 {file.FileName} 上传成功" });
}

// ========== 列出文件 ==========

public async Task<ActionResult> ListFiles(string directoryPath = null, string shareName = null)
{
    var result = await _fileService.ListFilesAndDirectoriesAsync(directoryPath, shareName);
    return Json(new
    {
        success = true,
        files = result.Files.Select(f => new { f.Name, f.Size, f.LastModified }),
        directories = result.Directories.Select(d => d.Name)
    }, JsonRequestBehavior.AllowGet);
}

// ========== 下载文件 ==========

public async Task<ActionResult> DownloadFile(string fileName, string directoryPath = null, string shareName = null)
{
    var memoryStream = new MemoryStream();
    await _fileService.DownloadToStreamAsync(fileName, memoryStream, directoryPath, shareName);
    memoryStream.Position = 0;

    return File(memoryStream, "application/octet-stream", fileName);
}

// ========== 删除文件 ==========

public async Task<ActionResult> DeleteFile(string fileName, string directoryPath = null, string shareName = null)
{
    bool deleted = await _fileService.DeleteFileAsync(fileName, directoryPath, shareName);
    return Json(new { success = deleted, message = deleted ? "删除成功" : "文件不存在" });
}

// ========== 删除目录 ==========

public async Task<ActionResult> DeleteDirectory(string directoryPath, string shareName = null)
{
    bool deleted = await _fileService.DeleteDirectoryAsync(directoryPath, shareName);
    return Json(new { success = deleted, message = deleted ? "目录删除成功" : "目录不存在" });
}

// ========== 批量下载 ZIP ==========

public async Task<ActionResult> DownloadAsZip(string directoryPath, string shareName = null)
{
    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    string zipFileName = $"download_{timestamp}.zip";

    var zipStream = await _fileService.DownloadAsZipStreamAsync(
        directoryPath,
        zipFileName: zipFileName,
        shareName: shareName);

    return File(zipStream,
        "application/zip",
        zipFileName);
}
```

### 4.3 Blob Storage Controller 示例

```csharp
// ========== 容器操作 ==========

public async Task<ActionResult> CreateContainer(string containerName, bool isPublic = false)
{
    var accessType = isPublic
        ? BlobContainerPublicAccessType.Container
        : BlobContainerPublicAccessType.Off;

    bool created = await _blobService.CreateContainerAsync(containerName, accessType);
    return Json(new { success = true, created, message = created ? "容器创建成功" : "容器已存在" });
}

public async Task<ActionResult> DeleteContainer(string containerName)
{
    bool deleted = await _blobService.DeleteContainerAsync(containerName);
    return Json(new { success = deleted, message = deleted ? "容器删除成功" : "容器不存在" });
}

// ========== 上传 Blob ==========

public async Task<ActionResult> UploadBlob()
{
    if (Request.Files.Count == 0)
        return Json(new { success = false, message = "未选择文件" });

    var file = Request.Files[0];
    string blobName = Request.Form["blobName"] ?? file.FileName;
    string containerName = Request.Form["containerName"];

    using (var stream = file.InputStream)
    {
        await _blobService.UploadFromStreamAsync(stream, blobName, containerName, file.ContentType);
    }

    return Json(new { success = true, message = $"Blob {blobName} 上传成功" });
}

// ========== 列出 Blob ==========

public async Task<ActionResult> ListBlobs(string prefix = null, string containerName = null)
{
    var blobs = await _blobService.ListBlobsAsync(prefix, containerName);
    return Json(new
    {
        success = true,
        items = blobs.Select(b => new { b.Name, b.Type, b.Size, b.LastModified })
    }, JsonRequestBehavior.AllowGet);
}

// ========== 下载 Blob ==========

public async Task<ActionResult> DownloadBlob(string blobName, string containerName = null)
{
    var memoryStream = new MemoryStream();
    await _blobService.DownloadToStreamAsync(blobName, memoryStream, containerName);
    memoryStream.Position = 0;

    // 尝试获取 Content-Type
    var props = await _blobService.GetBlobPropertiesAsync(blobName, containerName);
    string contentType = props.ContentType ?? "application/octet-stream";

    return File(memoryStream, contentType, Path.GetFileName(blobName));
}

// ========== 删除 Blob ==========

public async Task<ActionResult> DeleteBlob(string blobName, string containerName = null)
{
    bool deleted = await _blobService.DeleteBlobAsync(blobName, containerName);
    return Json(new { success = deleted, message = deleted ? "删除成功" : "Blob 不存在" });
}

// ========== 批量下载 ZIP ==========

public async Task<ActionResult> DownloadBlobsAsZip(string prefix = null, string containerName = null)
{
    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    string zipFileName = $"blob_download_{timestamp}.zip";

    var zipStream = await _blobService.DownloadAsZipStreamAsync(
        prefix,
        zipFileName: zipFileName,
        containerName: containerName);

    return File(zipStream,
        "application/zip",
        zipFileName);
}
```

---

## 5. Web.config 配置

```xml
<configuration>
  <connectionStrings>
    <add name="AzureStorageConnection"
         connectionString="DefaultEndpointsProtocol=https;AccountName=<your-account-name>;AccountKey=<your-account-key>;EndpointSuffix=core.windows.net" />
  </connectionStrings>

  <appSettings>
    <!-- Azure File Storage 默认文件共享名称 -->
    <add key="AzureFileShareName" value="my-file-share" />

    <!-- Azure Blob Storage 默认容器名称 -->
    <add key="AzureBlobContainerName" value="my-blob-container" />
  </appSettings>

  <system.web>
    <!-- 增大上传限制（根据需要调整） -->
    <httpRuntime maxRequestLength="1048576" executionTimeout="3600" />
  </system.web>

  <system.webServer>
    <security>
      <requestFiltering>
        <!-- IIS 上传限制（约 1GB） -->
        <requestLimits maxAllowedContentLength="1073741824" />
      </requestFiltering>
    </security>
  </system.webServer>
</configuration>
```

---

## 6. NuGet 包引用

通过 NuGet Package Manager Console 安装：

```powershell
# 核心 SDK（包含 File Storage 和 Blob Storage）
Install-Package WindowsAzure.Storage -Version 9.3.3

# 如果只需要 Blob Storage，也可以单独安装：
Install-Package Microsoft.Azure.Storage.Blob -Version 11.2.3

# JSON 序列化（Web API 返回 JSON 时可能需要）
Install-Package Newtonsoft.Json
```

或通过 `packages.config` 手动添加：

```xml
<packages>
  <package id="WindowsAzure.Storage" version="9.3.3" targetFramework="net472" />
  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net472" />
  <package id="Microsoft.Azure.Storage.Common" version="11.1.7" targetFramework="net472" />
</packages>
```

---

## 7. 常见问题与注意事项

### 7.1 连接字符串安全

| 建议 | 说明 |
|------|------|
| **不要硬编码** | 始终将连接字符串放在 Web.config 或 Azure Key Vault 中 |
| **不要提交到版本库** | 在 .gitignore 中排除 Web.config 或使用 config transforms |
| **使用 Managed Identity** | 部署到 Azure App Service 时优先使用托管标识 |

### 7.2 文件大小限制

| 存储类型 | 单文件上限 | 建议策略 |
|----------|-----------|---------|
| Azure File Storage | 4 TB（共享配额内） | SMB 协议可直接挂载 |
| Azure Blob Storage | 190.7 TB（Block Blob） | 大文件使用分块上传 |
| ASP.NET 上传 | 取决于配置 | 调整 `maxRequestLength` 和 `maxAllowedContentLength` |

### 7.3 并发与性能

- Azure File Storage 支持通过 SMB 协议并发访问，但高并发场景建议使用 Blob Storage
- Blob Storage 提供更高的吞吐量和可扩展性
- 批量 ZIP 下载时注意内存使用，大文件建议流式写入磁盘而非内存

### 7.4 ZIP 下载路径策略

| 方案 | 路径 | 适用场景 |
|------|------|---------|
| **App_Data（推荐）** | `~/App_Data/Downloads/` | 文件不直接对外暴露，通过 Controller 下载 |
| **临时目录** | `Path.GetTempPath()` | 短期使用，系统自动清理 |
| **专用下载目录** | `~/Downloads/` | 需配合 URL Rewrite 或权限控制 |

> **推荐使用 `DownloadAsZipStreamAsync` 方法**：直接返回 `MemoryStream` 给 Controller 的 `File()` 方法，避免文件落盘，更安全且无需清理。

### 7.5 错误处理最佳实践

```csharp
try
{
    await _fileService.UploadFileAsync(localPath, "documents/reports");
}
catch (StorageException ex) when (ex.RequestInformation.HttpStatusCode == 404)
{
    // 文件共享或目录不存在
    await _fileService.CreateDirectoryAsync("documents/reports");
    await _fileService.UploadFileAsync(localPath, "documents/reports");
}
catch (StorageException ex)
{
    // 记录详细错误信息
    string errorCode = ex.RequestInformation.ExtendedErrorInformation?.ErrorCode;
    string errorMessage = ex.RequestInformation.ExtendedErrorInformation?.ErrorMessage;
    // Log: $"Azure Storage Error: {errorCode} - {errorMessage}"
    throw;
}
```

### 7.6 两个服务的选择建议

| 对比维度 | Azure File Storage | Azure Blob Storage |
|----------|-------------------|-------------------|
| **文件系统语义** | 完整目录层级、文件锁定 | 扁平命名空间，虚拟文件夹 |
| **SMB/NFS 协议** | 支持，可直接挂载 | 不支持 |
| **可扩展性** | 5 TB/共享 | 无上限（账户级别） |
| **访问层级** | 标准/高级 | 热/冷/归档 |
| **适用场景** | 传统文件共享迁移、需要目录结构 | 静态资源、日志归档、大数据 |
| **成本** | 相对较高 | 相对较低 |

---

*文档结束。如有疑问请联系开发团队。*
