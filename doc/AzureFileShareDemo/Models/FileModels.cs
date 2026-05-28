using System;
using System.Collections.Generic;

namespace AzureFileShareDemo.Models
{
    /// <summary>
    /// 文件列表模型
    /// </summary>
    public class FileListModel
    {
        /// <summary>
        /// 当前路径
        /// </summary>
        public string CurrentPath { get; set; }

        /// <summary>
        /// 父目录路径
        /// </summary>
        public string ParentPath { get; set; }

        /// <summary>
        /// 文件和文件夹列表
        /// </summary>
        public List<FileItemModel> Items { get; set; } = new List<FileItemModel>();

        /// <summary>
        /// 下一页标记
        /// </summary>
        public string NextMarker { get; set; }

        /// <summary>
        /// 是否有更多文件
        /// </summary>
        public bool HasMore { get; set; }

        /// <summary>
        /// 总文件数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage { get; set; } = 1;
    }

    /// <summary>
    /// 文件项模型
    /// </summary>
    public class FileItemModel
    {
        /// <summary>
        /// 文件/文件夹名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 完整路径
        /// </summary>
        public string FullPath { get; set; }

        /// <summary>
        /// 是否是文件夹
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// 文件大小(字节)
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 格式化后的大小
        /// </summary>
        public string SizeFormatted
        {
            get
            {
                if (IsDirectory) return "-";
                
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = Size;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime? LastModified { get; set; }

        /// <summary>
        /// 内容类型
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// 文件类型图标
        /// </summary>
        public string FileTypeIcon
        {
            get
            {
                if (IsDirectory) return "folder";
                
                var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
                switch (ext)
                {
                    case ".pdf": return "file-pdf";
                    case ".doc":
                    case ".docx": return "file-doc";
                    case ".xls":
                    case ".xlsx": return "file-excel";
                    case ".ppt":
                    case ".pptx": return "file-ppt";
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                    case ".bmp": return "file-image";
                    case ".zip":
                    case ".rar":
                    case ".7z": return "file-archive";
                    case ".txt": return "file-text";
                    case ".mp3":
                    case ".wav":
                    case ".flac": return "file-audio";
                    case ".mp4":
                    case ".avi":
                    case ".mkv": return "file-video";
                    case ".html":
                    case ".htm": return "file-code";
                    case ".css":
                    case ".js":
                    case ".json": return "file-code";
                    default: return "file";
                }
            }
        }
    }

    /// <summary>
    /// 文件上传模型
    /// </summary>
    public class UploadModel
    {
        public string TargetPath { get; set; }
        public bool Overwrite { get; set; } = false;
    }

    /// <summary>
    /// 迁移结果模型
    /// </summary>
    public class MigrationResultModel
    {
        /// <summary>
        /// 上传成功数量
        /// </summary>
        public int UploadedCount { get; set; }

        /// <summary>
        /// 跳过数量(文件已存在)
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// 失败数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 失败的文件列表
        /// </summary>
        public List<MigrationError> Errors { get; set; } = new List<MigrationError>();

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 总耗时(秒)
        /// </summary>
        public double DurationSeconds => (EndTime - StartTime).TotalSeconds;

        /// <summary>
        /// 总字节数
        /// </summary>
        public long TotalBytes { get; set; }
    }

    /// <summary>
    /// 迁移错误记录
    /// </summary>
    public class MigrationError
    {
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 存储账户信息模型
    /// </summary>
    public class StorageInfoModel
    {
        public string AccountName { get; set; }
        public string ShareName { get; set; }
        public string Endpoint { get; set; }
        public DateTime? LastModified { get; set; }
        public long UsageBytes { get; set; }
        public long QuotaBytes { get; set; }
        public double UsagePercentage => QuotaBytes > 0 ? (double)UsageBytes / QuotaBytes * 100 : 0;
    }

    /// <summary>
    /// 搜索结果模型
    /// </summary>
    public class SearchResultModel
    {
        public string Keyword { get; set; }
        public List<FileItemModel> Results { get; set; } = new List<FileItemModel>();
        public int TotalCount { get; set; }
        public TimeSpan SearchTime { get; set; }
    }

    /// <summary>
    /// 操作日志模型
    /// </summary>
    public class OperationLogModel
    {
        public string Operation { get; set; }
        public string FilePath { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }
    }
}
