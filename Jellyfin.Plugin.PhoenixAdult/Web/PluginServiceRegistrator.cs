#if !__EMBY__
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// 注册抽帧功能需要的服务：脚本注入过滤器 + 插件自带的 API 控制器。
    /// Emby 没有这套机制，整个 Web 目录在 Emby 构建里被排除。
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc/>
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
            serviceCollection.AddControllers().AddApplicationPart(typeof(FrameGrabController).Assembly);

            // 需要 Referer 的图片走插件自己的代理端点：Jellyfin 下载图片时要绝对地址，这里记住服务端自己的 API 根地址
            ImageProxyUrl.BaseUrl = applicationHost?.GetLocalApiUrl("127.0.0.1", null, null)
                ?? applicationHost?.GetSmartApiUrl("localhost");
        }
    }
}
#endif
