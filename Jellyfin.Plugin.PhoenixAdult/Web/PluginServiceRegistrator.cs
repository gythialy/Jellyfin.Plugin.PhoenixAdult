#if !__EMBY__
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhoenixAdult.Helpers;
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
            // Remember the server API root for the image proxy; every loaded copy keeps its own static.
            ImageProxyUrl.BaseUrl = applicationHost?.GetLocalApiUrl("127.0.0.1", null, null)
                ?? applicationHost?.GetSmartApiUrl("localhost");

            // A manual build left next to the auto-updated one loads this assembly twice and both
            // copies would add the same controller, so only the newest copy owns the endpoints.
            var ownsEndpoints = FrameGrabRegistration.ShouldOwnEndpoints(
                typeof(PluginServiceRegistrator).Assembly,
                out var duplicates);

            if (!ownsEndpoints)
            {
                return;
            }

            if (duplicates.Length > 0)
            {
                Logger.Warning(
                    $"PhoenixAdult is installed more than once: {string.Join(", ", duplicates)}. "
                    + "The web client entry is served by one copy only; remove the extra plugin folder.");
            }

            serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
            serviceCollection.AddControllers().AddApplicationPart(typeof(FrameGrabController).Assembly);
        }
    }
}
#endif
