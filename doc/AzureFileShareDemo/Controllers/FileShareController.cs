using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using AzureFileShareDemo.Models;
using AzureFileShareDemo.Services;
using Microsoft.WindowsAzure.Storage.File;

namespace AzureFileShareDemo.Controllers
{
    /// <summary>
    /// Azure File Share 文件管理控制器
    /// 提供文件浏览、上传、下载、删除、迁移等功能
    /// </summary>
    public class FileShareController : Controller
    {
        private readonly IAzureFileShareService _fileShareService;
        
        // 每页显示的文件数量
        private const int PageSize = 50;

        public FileShareController()
        {
            _fileShareService = new AzureFileShareService();
        }

        /// <summary>
        /// 首页 - 显示根目录文件列表
        /// </summary>
        public async Task<ActionResult> Index(string path = "")
        {
            try
            {
                var model = await _fileShareService.ListDirectoryAsync(path, PageSize);
                return View(model);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"加载文件列表失败: {ex.Message}";
                return View(new FileListModel());
            }
        }

        /// <summary>
        /// 分页加载更多文件
        /// </summary>
        public async Task<ActionResult> LoadMore(string path, string marker)
        {
            try
            {
                var model = await _fileShareService.ListDirectoryAsync(path, PageSize, marker);
                return PartialView("_FileList", model);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 上传文件页面
        /// </summary>
        public ActionResult Upload(string path = "")
        {
            ViewBag.CurrentPath = path;
            return View();
        }

        /// <summary>
        /// 上传文件到Azure File Share
        /// </summary>
        [HttpPost]
        [ValidateInput(false)]
        public async Task<ActionResult> UploadFile(HttpPostedFileBase file, string targetPath = "")
        {
            if (file == null || file.ContentLength == 0)
            {
                return Json(new { success = false, message = "请选择要上传的文件" });
            }

            try
            {
                var fileName = Path.GetFileName(file.FileName);
                var fullPath = string.IsNullOrEmpty(targetPath) 
                    ? fileName 
                    : $"{targetPath.TrimEnd('/')}/{fileName}";

                using (var stream = file.InputStream)
                {
                    await _fileShareService.UploadFileAsync(fullPath, stream, file.ContentLength);
                }

                return Json(new { success = true, message = "文件上传成功", fileName });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"上传失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 批量上传文件
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> UploadMultiple(IEnumerable<HttpPostedFileBase> files, string targetPath = "")
        {
            var results = new List<object>();
            int successCount = 0;
            int failCount = 0;

            foreach (var file in files)
            {
                if (file != null && file.ContentLength > 0)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file.FileName);
                        var fullPath = string.IsNullOrEmpty(targetPath) 
                            ? fileName 
                            : $"{targetPath.TrimEnd('/')}/{fileName}";

                        using (var stream = file.InputStream)
                        {
                            await _fileShareService.UploadFileAsync(fullPath, stream, file.ContentLength);
                        }
                        successCount++;
                        results.Add(new { fileName, status = "success" });
                    }
                    catch
                    {
                        failCount++;
                        results.Add(new { fileName = file.FileName, status = "failed" });
                    }
                }
            }

            return Json(new { 
                success = failCount == 0, 
                message = $"成功: {successCount}, 失败: {failCount}",
                details = results
            });
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        public async Task<ActionResult> Download(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new HttpStatusCodeResult(400, "文件路径不能为空");
            }

            try
            {
                var fileInfo = await _fileShareService.GetFileInfoAsync(path);
                var stream = new MemoryStream();
                await _fileShareService.DownloadFileAsync(path, stream);
                stream.Position = 0;

                return File(stream, "application/octet-stream", fileInfo.Name);
            }
            catch (Exception ex)
            {
                return new HttpStatusCodeResult(500, ex.Message);
            }
        }

        /// <summary>
        /// 创建文件夹
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> CreateDirectory(string path, string directoryName)
        {
            if (string.IsNullOrEmpty(directoryName))
            {
                return Json(new { success = false, message = "文件夹名称不能为空" });
            }

            try
            {
                var fullPath = string.IsNullOrEmpty(path) 
                    ? directoryName 
                    : $"{path.TrimEnd('/')}/{directoryName}";

                await _fileShareService.CreateDirectoryAsync(fullPath);
                return Json(new { success = true, message = "文件夹创建成功" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"创建失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> Delete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Json(new { success = false, message = "文件路径不能为空" });
            }

            try
            {
                await _fileShareService.DeleteAsync(path);
                return Json(new { success = true, message = "删除成功" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"删除失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 批量删除文件
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> DeleteMultiple(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return Json(new { success = false, message = "请选择要删除的文件" });
            }

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            foreach (var path in paths)
            {
                try
                {
                    await _fileShareService.DeleteAsync(path);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    errors.Add($"{path}: {ex.Message}");
                }
            }

            return Json(new { 
                success = failCount == 0, 
                message = $"成功删除: {successCount}, 失败: {failCount}",
                errors = errors
            });
        }

        /// <summary>
        /// 移动或重命名文件
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> Move(string sourcePath, string targetPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
            {
                return Json(new { success = false, message = "源路径和目标路径都不能为空" });
            }

            try
            {
                await _fileShareService.MoveFileAsync(sourcePath, targetPath);
                return Json(new { success = true, message = "移动成功" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"移动失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 复制文件
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> Copy(string sourcePath, string targetPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
            {
                return Json(new { success = false, message = "源路径和目标路径都不能为空" });
            }

            try
            {
                await _fileShareService.CopyFileAsync(sourcePath, targetPath);
                return Json(new { success = true, message = "复制成功" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"复制失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取文件信息
        /// </summary>
        public async Task<ActionResult> GetFileInfo(string path)
        {
            try
            {
                var fileInfo = await _fileShareService.GetFileInfoAsync(path);
                return Json(new { 
                    success = true, 
                    data = new {
                        name = fileInfo.Name,
                        length = fileInfo.Length,
                        lastModified = fileInfo.LastModified,
                        contentType = fileInfo.Properties.ContentType,
                        uri = fileInfo.Uri.ToString()
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 本地到Azure文件迁移页面
        /// </summary>
        public ActionResult Migrate()
        {
            return View();
        }

        /// <summary>
        /// 开始文件迁移任务
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> StartMigration(string localPath, string azurePath, bool includeSubdirectories = true)
        {
            if (string.IsNullOrEmpty(localPath) || !System.IO.Directory.Exists(localPath))
            {
                return Json(new { success = false, message = "本地路径无效或不存在" });
            }

            try
            {
                var result = await _fileShareService.MigrateFromLocalAsync(localPath, azurePath, includeSubdirectories);
                return Json(new { 
                    success = true, 
                    message = $"迁移完成: {result.UploadedCount} 个文件成功, {result.FailedCount} 个失败",
                    details = result
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"迁移失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取迁移进度
        /// </summary>
        public ActionResult GetMigrationProgress(string taskId)
        {
            // 实际应用中应该从缓存或数据库获取进度
            return Json(new { taskId, progress = 100, status = "completed" }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// 同步本地目录到Azure
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> SyncToAzure(string localPath, string azurePath)
        {
            try
            {
                var result = await _fileShareService.SyncDirectoryAsync(localPath, azurePath);
                return Json(new { 
                    success = true,
                    uploaded = result.UploadedCount,
                    skipped = result.SkippedCount,
                    failed = result.FailedCount
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 生成文件分享链接
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> GenerateShareLink(string path, int validHours = 24)
        {
            try
            {
                var sasToken = await _fileShareService.GenerateSasTokenAsync(path, validHours);
                return Json(new { success = true, sasToken });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 搜索文件
        /// </summary>
        public async Task<ActionResult> Search(string keyword, string path = "")
        {
            try
            {
                var results = await _fileShareService.SearchFilesAsync(keyword, path);
                return View("SearchResults", results);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View("SearchResults", new List<FileItemModel>());
            }
        }

        /// <summary>
        /// 获取存储账户信息
        /// </summary>
        public async Task<ActionResult> GetStorageInfo()
        {
            try
            {
                var info = await _fileShareService.GetStorageAccountInfoAsync();
                return Json(new { success = true, data = info }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
