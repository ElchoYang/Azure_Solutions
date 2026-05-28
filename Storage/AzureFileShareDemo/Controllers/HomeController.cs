using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using AzureFileShareDemo.Services;

namespace AzureFileShareDemo.Controllers
{
    /// <summary>
    /// 首页控制器
    /// </summary>
    public class HomeController : Controller
    {
        private readonly IAzureFileShareService _fileShareService;

        public HomeController()
        {
            _fileShareService = new AzureFileShareService();
        }

        /// <summary>
        /// 首页
        /// </summary>
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// 关于页面
        /// </summary>
        public ActionResult About()
        {
            ViewBag.Message = "Azure File Share Demo Application";
            return View();
        }

        /// <summary>
        /// 联系页面
        /// </summary>
        public ActionResult Contact()
        {
            ViewBag.Message = "Contact Information";
            return View();
        }

        /// <summary>
        /// 获取仪表盘数据
        /// </summary>
        public async Task<ActionResult> Dashboard()
        {
            try
            {
                var info = await _fileShareService.GetStorageAccountInfoAsync();
                return View(info);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View();
            }
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        public async Task<ActionResult> HealthCheck()
        {
            try
            {
                await _fileShareService.CheckConnectionAsync();
                return Json(new { status = "healthy", timestamp = DateTime.Now }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { status = "unhealthy", error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
