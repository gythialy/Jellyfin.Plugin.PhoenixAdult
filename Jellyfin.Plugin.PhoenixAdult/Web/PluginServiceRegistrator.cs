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
    /// Register the script injection filter and the plugin controllers (Jellyfin only).
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc/>
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
            serviceCollection.AddControllers().AddApplicationPart(typeof(FrameGrabController).Assembly);

            // Remember the server API root for the image proxy.
            ImageProxyUrl.BaseUrl = applicationHost?.GetLocalApiUrl("127.0.0.1", null, null)
                ?? applicationHost?.GetSmartApiUrl("localhost");
        }
    }
}
#endif
