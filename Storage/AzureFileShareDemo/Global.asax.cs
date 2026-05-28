using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace AzureFileShareDemo
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            
            // 初始化Azure存储连接
            InitializeAzureStorage();
        }

        /// <summary>
        /// 初始化Azure存储连接
        /// </summary>
        private void InitializeAzureStorage()
        {
            try
            {
                var service = new Services.AzureFileShareService();
                var isConnected = service.CheckConnectionAsync().Result;
                
                if (isConnected)
                {
                    System.Diagnostics.Debug.WriteLine("Azure File Share 连接成功");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Azure File Share 连接失败，请检查配置");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Azure Storage 初始化错误: {ex.Message}");
            }
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            Exception exception = Server.GetLastError();
            
            if (exception != null)
            {
                // 记录日志
                Elmah.ErrorSignal.FromCurrentContext().Raise(exception);
                
                // 处理不同的异常类型
                if (exception is HttpException httpEx)
                {
                    Response.StatusCode = httpEx.GetHttpCode();
                }
                else
                {
                    Response.StatusCode = 500;
                }
            }
        }
    }

    /// <summary>
    /// 路由配置
    /// </summary>
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }

    /// <summary>
    /// 过滤器配置
    /// </summary>
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }

    /// <summary>
    /// Bundle配置
    /// </summary>
    public class BundleConfig
    {
        public static void RegisterBundles(BundleCollection bundles)
        {
            // jQuery Bundle
            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                "~/Scripts/jquery-{version}.js"));

            // Bootstrap Bundle
            bundles.Add(new ScriptBundle("~/bundles/bootstrap").Include(
                "~/Scripts/bootstrap.js",
                "~/Scripts/respond.js"));

            // CSS Bundle
            bundles.Add(new StyleBundle("~/Content/css").Include(
                "~/Content/bootstrap.css",
                "~/Content/Site.css"));

            // 启用优化
            BundleTable.EnableOptimizations = true;
        }
    }
}
