using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using AzureFileShareDemo.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.File;

namespace AzureFileShareDemo.Services
{
    /// <summary>
    /// Azure File Share 服务接口
    /// </summary>
    public interface IAzureFileShareService
    {
        /// <summary>
        /// 获取云文件目录引用
        /// </summary>
        CloudFileDirectory GetRootDirectory();

        /// <summary>
        /// 列出目录下的文件和子目录
        /// </summary>
        Task<FileListModel> ListDirectoryAsync(string path, int maxResults = 50, string marker = null);

        /// <summary>
        /// 上传文件
        /// </summary>
        Task UploadFileAsync(string path, Stream stream, long? fileSize = null);

        /// <summary>
        /// 上传本地文件
        /// </summary>
        Task UploadFileAsync(string azurePath, string localFilePath);

        /// <summary>
        /// 下载文件到流
        /// </summary>
        Task DownloadFileAsync(string path, Stream outputStream);

        /// <summary>
        /// 下载文件到本地路径
        /// </summary>
        Task DownloadFileAsync(string azurePath, string localPath);

        /// <summary>
        /// 创建目录
        /// </summary>
        Task CreateDirectoryAsync(string path);

        /// <summary>
        /// 删除文件或目录
        /// </summary>
        Task DeleteAsync(string path);

        /// <summary>
        /// 获取文件信息
        /// </summary>
        Task<IListFileItem> GetFileInfoAsync(string path);

        /// <summary>
        /// 移动或重命名文件
        /// </summary>
        Task MoveFileAsync(string sourcePath, string targetPath);

        /// <summary>
        /// 复制文件
        /// </summary>
        Task CopyFileAsync(string sourcePath, string targetPath);

        /// <summary>
        /// 从本地迁移到Azure
        /// </summary>
        Task<MigrationResultModel> MigrateFromLocalAsync(string localPath, string azurePath, bool includeSubdirectories = true);

        /// <summary>
        /// 同步目录(仅上传新文件或修改过的文件)
        /// </summary>
        Task<MigrationResultModel> SyncDirectoryAsync(string localPath, string azurePath);

        /// <summary>
        /// 搜索文件
        /// </summary>
        Task<IOrderedEnumerable<FileItemModel>> SearchFilesAsync(string keyword, string path = "");

        /// <summary>
        /// 生成SAS令牌
        /// </summary>
        Task<string> GenerateSasTokenAsync(string path, int validHours = 24);

        /// <summary>
        /// 获取存储账户信息
        /// </summary>
        Task<StorageInfoModel> GetStorageAccountInfoAsync();

        /// <summary>
        /// 检查连接
        /// </summary>
        Task<bool> CheckConnectionAsync();

        /// <summary>
        /// 获取文件分享URI
        /// </summary>
        string GetShareUri();
    }

    /// <summary>
    /// Azure File Share 服务实现
    /// </summary>
    public class AzureFileShareService : IAzureFileShareService
    {
        private readonly CloudFileClient _fileClient;
        private readonly CloudFileShare _fileShare;
        private readonly string _accountName;
        private readonly string _accountKey;

        // 配置 - 建议从Web.config读取
        private const string StorageAccountName = "your_storage_account_name";
        private const string StorageAccountKey = "your_storage_account_key";
        private const string FileShareName = "your_file_share_name";

        public AzureFileShareService()
        {
            _accountName = StorageAccountName;
            _accountKey = StorageAccountKey;

            // 从配置文件读取
            var connString = System.Configuration.ConfigurationManager.ConnectionStrings["AzureStorage"]?.ConnectionString;
            if (!string.IsNullOrEmpty(connString))
            {
                var account = CloudStorageAccount.Parse(connString);
                _fileClient = account.CreateCloudFileClient();
            }
            else
            {
                var credentials = new StorageCredentials(_accountName, _accountKey);
                var account = new CloudStorageAccount(credentials, "core.windows.net", true);
                _fileClient = account.CreateCloudFileClient();
            }

            _fileShare = _fileClient.GetShareReference(FileShareName);
        }

        /// <summary>
        /// 使用连接字符串初始化
        /// </summary>
        public AzureFileShareService(string connectionString)
        {
            var account = CloudStorageAccount.Parse(connectionString);
            _fileClient = account.CreateCloudFileClient();
            
            // 从连接字符串解析账户名
            var parts = connectionString.Split(';');
            var accountPart = parts.FirstOrDefault(p => p.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase));
            _accountName = accountPart?.Split('=').Last() ?? "";

            // 获取第一个共享
            _fileShare = _fileClient.GetShareReference(FileShareName);
        }

        /// <summary>
        /// 获取根目录引用
        /// </summary>
        public CloudFileDirectory GetRootDirectory()
        {
            return _fileShare.GetRootDirectoryReference();
        }

        /// <summary>
        /// 获取指定路径的目录引用
        /// </summary>
        private CloudFileDirectory GetDirectoryReference(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return _fileShare.GetRootDirectoryReference();
            }

            var dir = _fileShare.GetRootDirectoryReference();
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var segment in segments)
            {
                dir = dir.GetDirectoryReference(segment);
            }

            return dir;
        }

        /// <summary>
        /// 获取指定路径的文件引用
        /// </summary>
        private CloudFile GetFileReference(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("文件路径不能为空");
            }

            var dir = _fileShare.GetRootDirectoryReference();
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (segments.Length == 0)
            {
                throw new ArgumentException("无效的文件路径");
            }

            // 最后一个分段是文件名
            var fileName = segments.Last();
            
            // 获取父目录
            for (int i = 0; i < segments.Length - 1; i++)
            {
                dir = dir.GetDirectoryReference(segments[i]);
            }

            return dir.GetFileReference(fileName);
        }

        /// <summary>
        /// 列出目录下的文件和子目录
        /// </summary>
        public async Task<FileListModel> ListDirectoryAsync(string path, int maxResults = 50, string marker = null)
        {
            var model = new FileListModel
            {
                CurrentPath = path,
                ParentPath = GetParentPath(path)
            };

            var directory = GetDirectoryReference(path);
            
            // 确保目录存在
            await directory.CreateIfNotExistsAsync();

            FileContinuationToken token = null;
            if (!string.IsNullOrEmpty(marker))
            {
                token = new FileContinuationToken { NextMarker = marker };
            }

            var result = await directory.ListFilesAndDirectoriesSegmentedAsync(maxResults, token);
            
            foreach (var item in result.Results)
            {
                var fileItem = new FileItemModel
                {
                    FullPath = item.Uri.AbsoluteUri.Contains(_fileShare.Name) 
                        ? GetRelativePath(item.Uri.AbsoluteUri) 
                        : item.Uri.ToString()
                };

                if (item is CloudFile)
                {
                    var file = (CloudFile)item;
                    await file.FetchAttributesAsync();
                    fileItem.Name = file.Name;
                    fileItem.IsDirectory = false;
                    fileItem.Size = file.Properties.Length;
                    fileItem.LastModified = file.Properties.LastModified;
                    fileItem.ContentType = file.Properties.ContentType;
                }
                else if (item is CloudFileDirectory)
                {
                    var dir = (CloudFileDirectory)item;
                    fileItem.Name = dir.Name;
                    fileItem.IsDirectory = true;
                    fileItem.LastModified = null;
                }

                model.Items.Add(fileItem);
            }

            model.NextMarker = result.ContinuationToken?.NextMarker;
            model.HasMore = !string.IsNullOrEmpty(model.NextMarker);
            model.TotalCount = model.Items.Count;

            // 按文件夹优先排序
            model.Items = model.Items
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name)
                .ToList();

            return model;
        }

        /// <summary>
        /// 上传文件到Azure File Share
        /// </summary>
        public async Task UploadFileAsync(string path, Stream stream, long? fileSize = null)
        {
            var file = GetFileReference(path);
            
            // 确保父目录存在
            var parentDir = file.Parent;
            await parentDir.CreateIfNotExistsAsync();

            await file.UploadFromStreamAsync(stream);
            
            // 设置文件属性
            if (fileSize.HasValue)
            {
                file.Properties.ContentLength = fileSize.Value;
            }
            
            await file.SetPropertiesAsync();
        }

        /// <summary>
        /// 上传本地文件到Azure
        /// </summary>
        public async Task UploadFileAsync(string azurePath, string localFilePath)
        {
            if (!System.IO.File.Exists(localFilePath))
            {
                throw new FileNotFoundException($"本地文件不存在: {localFilePath}");
            }

            var file = GetFileReference(azurePath);
            
            // 确保父目录存在
            var parentDir = file.Parent;
            await parentDir.CreateIfNotExistsAsync();

            await file.UploadFromFileAsync(localFilePath, FileMode.Open);
        }

        /// <summary>
        /// 下载文件到流
        /// </summary>
        public async Task DownloadFileAsync(string path, Stream outputStream)
        {
            var file = GetFileReference(path);
            
            // 检查文件是否存在
            if (!await file.ExistsAsync())
            {
                throw new FileNotFoundException($"Azure文件不存在: {path}");
            }

            await file.DownloadToStreamAsync(outputStream);
            outputStream.Position = 0;
        }

        /// <summary>
        /// 下载文件到本地路径
        /// </summary>
        public async Task DownloadFileAsync(string azurePath, string localPath)
        {
            var file = GetFileReference(azurePath);
            
            if (!await file.ExistsAsync())
            {
                throw new FileNotFoundException($"Azure文件不存在: {azurePath}");
            }

            // 确保本地目录存在
            var localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }

            await file.DownloadToFileAsync(localPath, FileMode.Create);
        }

        /// <summary>
        /// 创建目录
        /// </summary>
        public async Task CreateDirectoryAsync(string path)
        {
            var directory = GetDirectoryReference(path);
            await directory.CreateIfNotExistsAsync();
        }

        /// <summary>
        /// 删除文件或目录
        /// </summary>
        public async Task DeleteAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("路径不能为空");
            }

            // 尝试作为文件删除
            var file = GetFileReference(path);
            if (await file.ExistsAsync())
            {
                await file.DeleteAsync();
                return;
            }

            // 尝试作为目录删除
            var directory = GetDirectoryReference(path);
            if (await directory.ExistsAsync())
            {
                await directory.DeleteAsync();
                return;
            }

            throw new FileNotFoundException($"路径不存在: {path}");
        }

        /// <summary>
        /// 获取文件信息
        /// </summary>
        public async Task<IListFileItem> GetFileInfoAsync(string path)
        {
            var file = GetFileReference(path);
            
            if (await file.ExistsAsync())
            {
                await file.FetchAttributesAsync();
                return file;
            }

            var directory = GetDirectoryReference(path);
            if (await directory.ExistsAsync())
            {
                return directory;
            }

            throw new FileNotFoundException($"路径不存在: {path}");
        }

        /// <summary>
        /// 移动或重命名文件(通过下载再上传再删除实现)
        /// </summary>
        public async Task MoveFileAsync(string sourcePath, string targetPath)
        {
            // 下载到内存
            using (var stream = new MemoryStream())
            {
                await DownloadFileAsync(sourcePath, stream);
                
                // 获取目标文件引用并确保目录存在
                var targetFile = GetFileReference(targetPath);
                await targetFile.Parent.CreateIfNotExistsAsync();
                
                // 上传到目标位置
                await targetFile.UploadFromStreamAsync(stream);
            }

            // 删除源文件
            await DeleteAsync(sourcePath);
        }

        /// <summary>
        /// 复制文件(通过下载再上传实现)
        /// </summary>
        public async Task CopyFileAsync(string sourcePath, string targetPath)
        {
            // 下载到内存
            using (var stream = new MemoryStream())
            {
                await DownloadFileAsync(sourcePath, stream);
                
                // 获取目标文件引用并确保目录存在
                var targetFile = GetFileReference(targetPath);
                await targetFile.Parent.CreateIfNotExistsAsync();
                
                // 上传到目标位置
                await targetFile.UploadFromStreamAsync(stream);
            }
        }

        /// <summary>
        /// 从本地文件系统迁移到Azure
        /// </summary>
        public async Task<MigrationResultModel> MigrateFromLocalAsync(string localPath, string azurePath, bool includeSubdirectories = true)
        {
            var result = new MigrationResultModel
            {
                StartTime = DateTime.Now
            };

            if (!Directory.Exists(localPath))
            {
                throw new DirectoryNotFoundException($"本地目录不存在: {localPath}");
            }

            var searchOption = includeSubdirectories 
                ? SearchOption.AllDirectories 
                : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(localPath, "*", searchOption);
            var localRoot = localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var localFile in files)
            {
                try
                {
                    // 计算相对路径
                    var relativePath = localFile.Substring(localRoot.Length + 1);
                    var azureFilePath = string.IsNullOrEmpty(azurePath) 
                        ? relativePath 
                        : $"{azurePath}/{relativePath}";

                    // 替换路径分隔符
                    azureFilePath = azureFilePath.Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');

                    await UploadFileAsync(azureFilePath, localFile);
                    
                    result.UploadedCount++;
                    var fileInfo = new FileInfo(localFile);
                    result.TotalBytes += fileInfo.Length;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add(new MigrationError
                    {
                        FilePath = localFile,
                        ErrorMessage = ex.Message
                    });
                }
            }

            result.EndTime = DateTime.Now;
            return result;
        }

        /// <summary>
        /// 同步目录(仅上传新文件和修改过的文件)
        /// </summary>
        public async Task<MigrationResultModel> SyncDirectoryAsync(string localPath, string azurePath)
        {
            var result = new MigrationResultModel
            {
                StartTime = DateTime.Now
            };

            if (!Directory.Exists(localPath))
            {
                throw new DirectoryNotFoundException($"本地目录不存在: {localPath}");
            }

            var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
            var localRoot = localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var localFile in files)
            {
                try
                {
                    // 计算相对路径
                    var relativePath = localFile.Substring(localRoot.Length + 1);
                    var azureFilePath = string.IsNullOrEmpty(azurePath) 
                        ? relativePath 
                        : $"{azurePath}/{relativePath}";

                    azureFilePath = azureFilePath.Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');

                    // 检查是否需要上传
                    var shouldUpload = await ShouldUploadFileAsync(azureFilePath, localFile);
                    
                    if (shouldUpload)
                    {
                        await UploadFileAsync(azureFilePath, localFile);
                        result.UploadedCount++;
                    }
                    else
                    {
                        result.SkippedCount++;
                    }

                    var fileInfo = new FileInfo(localFile);
                    result.TotalBytes += fileInfo.Length;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add(new MigrationError
                    {
                        FilePath = localFile,
                        ErrorMessage = ex.Message
                    });
                }
            }

            result.EndTime = DateTime.Now;
            return result;
        }

        /// <summary>
        /// 检查是否应该上传文件(基于大小和修改时间)
        /// </summary>
        private async Task<bool> ShouldUploadFileAsync(string azurePath, string localFilePath)
        {
            var localInfo = new FileInfo(localFilePath);
            var localSize = localInfo.Length;
            var localModified = localInfo.LastWriteTimeUtc;

            try
            {
                var azureFile = GetFileReference(azurePath);
                
                if (!await azureFile.ExistsAsync())
                {
                    return true; // 文件不存在，需要上传
                }

                await azureFile.FetchAttributesAsync();
                
                // 比较文件大小和修改时间
                if (azureFile.Properties.Length != localSize)
                {
                    return true; // 大小不同，需要上传
                }

                // 注意: Azure File Share 不支持 Last-Modified 的精确比较
                // 这里仅做大小比较
                return false; // 文件相同，跳过
            }
            catch
            {
                return true; // 出错时假设需要上传
            }
        }

        /// <summary>
        /// 搜索文件(递归搜索)
        /// </summary>
        public async Task<IOrderedEnumerable<FileItemModel>> SearchFilesAsync(string keyword, string path = "")
        {
            var results = new List<FileItemModel>();
            await SearchDirectoryAsync(path, keyword.ToLowerInvariant(), results);
            
            return results.OrderBy(x => x.Name);
        }

        /// <summary>
        /// 递归搜索目录
        /// </summary>
        private async Task SearchDirectoryAsync(string path, string keyword, List<FileItemModel> results)
        {
            try
            {
                var model = await ListDirectoryAsync(path, 1000);
                
                foreach (var item in model.Items)
                {
                    if (item.Name.ToLowerInvariant().Contains(keyword))
                    {
                        results.Add(item);
                    }

                    if (item.IsDirectory)
                    {
                        await SearchDirectoryAsync(item.FullPath, keyword, results);
                    }
                }
            }
            catch (Exception)
            {
                // 忽略访问错误，继续搜索其他目录
            }
        }

        /// <summary>
        /// 生成SAS令牌
        /// </summary>
        public async Task<string> GenerateSasTokenAsync(string path, int validHours = 24)
        {
            CloudFile file;
            
            if (string.IsNullOrEmpty(path))
            {
                file = _fileShare.GetRootDirectoryReference().GetFileReference("");
            }
            else
            {
                file = GetFileReference(path);
            }

            if (await file.ExistsAsync())
            {
                var sasConstraints = new SharedAccessFileHeaders
                {
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(validHours)
                };

                var sasToken = file.GetSharedAccessSignature(
                    SharedAccessFilePermissions.Read,
                    null, // 起始时间
                    DateTimeOffset.UtcNow.AddHours(validHours), // 结束时间
                    sasConstraints);

                return sasToken;
            }
            else
            {
                // 目录的SAS
                var dir = GetDirectoryReference(path);
                var sasToken = dir.GetSharedAccessSignature(
                    SharedAccessFilePermissions.Read | SharedAccessFilePermissions.List,
                    null,
                    DateTimeOffset.UtcNow.AddHours(validHours));

                return sasToken;
            }
        }

        /// <summary>
        /// 获取存储账户信息
        /// </summary>
        public async Task<StorageInfoModel> GetStorageAccountInfoAsync()
        {
            await _fileShare.FetchAttributesAsync();

            return new StorageInfoModel
            {
                AccountName = _accountName,
                ShareName = _fileShare.Name,
                Endpoint = _fileShare.Uri.ToString(),
                LastModified = _fileShare.Properties.LastModified,
                UsageBytes = _fileShare.Properties.UsageBytes,
                QuotaBytes = _fileShare.Properties.Quota
            };
        }

        /// <summary>
        /// 检查连接是否正常
        /// </summary>
        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                await _fileShare.ExistsAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取共享URI
        /// </summary>
        public string GetShareUri()
        {
            return _fileShare.Uri.ToString();
        }

        /// <summary>
        /// 获取相对路径
        /// </summary>
        private string GetRelativePath(string absoluteUri)
        {
            var shareUri = _fileShare.Uri.AbsoluteUri.TrimEnd('/');
            var fileUri = absoluteUri.Replace(shareUri + "/", "").Replace(shareUri, "");
            return HttpUtility.UrlDecode(fileUri);
        }

        /// <summary>
        /// 获取父路径
        /// </summary>
        private string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 1)
            {
                return null;
            }

            return string.Join("/", segments.Take(segments.Length - 1));
        }
    }
}
