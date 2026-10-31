using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using PhoenixAdult.Configuration;

#if __EMBY__
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
#else
using System.Net.Http;
using Microsoft.Extensions.Logging;

// using Sentry;
#endif

[assembly: CLSCompliant(false)]

namespace PhoenixAdult
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
#if __EMBY__
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IHttpClient http, ILogManager logger)
#else
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IHttpClientFactory http, ILogger<Plugin> logger)
#endif
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Http = http;

#if __EMBY__
            if (logger != null)
            {
                Log = logger.GetLogger(this.Name);
            }
#else
            Log = logger;
            this.ConfigurationChanged += PluginConfiguration.ConfigurationChanged;

            /*
            SentrySdk.Init(new SentryOptions
            {
                Dsn = Consts.SentryDSN,
            });
            */
#endif

            this.EnsureImageRefererDefaults();

#if !__EMBY__
            Helpers.Logger.Info($"Image proxy base URL: {Helpers.Utils.ImageProxyUrl.BaseUrl ?? "(unavailable)"}");
#endif
        }

#if __EMBY__
        public static IHttpClient Http { get; set; }
#else
        public static IHttpClientFactory Http { get; set; }
#endif

        public static ILogger Log { get; set; }

        /// <summary>
        /// Seed the built-in referer rules into an empty configuration so they appear in the settings page.
        /// </summary>
        private void EnsureImageRefererDefaults()
        {
            var config = this.Configuration;
            if (config == null)
            {
                return;
            }

            var seeded = ImageRefererDefaults.SeedIfEmpty(config.ImageReferers);
            if (seeded == null)
            {
                return;
            }

            config.ImageReferers = seeded;
            this.UpdateConfiguration(config);
#if __EMBY__
            Log?.Info($"Seeded default image referer rules: {string.Join(", ", config.ImageReferers)}");
#else
            Log?.LogInformation("Seeded default image referer rules: {Rules}", string.Join(", ", config.ImageReferers));
#endif
        }

        public static Plugin Instance { get; private set; }

        public override string Name => "PhoenixAdult";

        public override Guid Id => Guid.Parse("8f97371f-8617-463c-9859-a33072182494");

        public IEnumerable<PluginPageInfo> GetPages()
            => new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = $"{this.GetType().Namespace}.Configuration.configPage.html",
                },
            };
    }
}
