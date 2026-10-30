#if !__EMBY__
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
        }
    }
}
#endif
