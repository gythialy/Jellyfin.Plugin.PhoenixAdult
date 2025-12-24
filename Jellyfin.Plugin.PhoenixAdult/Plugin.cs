using System;
using System.Collections.Generic;
using System.Net.Http;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using PhoenixAdult.Configuration;

[assembly: CLSCompliant(false)]

namespace PhoenixAdult
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IHttpClientFactory http, ILogger<Plugin> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Http = http;

            Log = logger;
            this.ConfigurationChanged += PluginConfiguration.ConfigurationChanged;
        }

        public static IHttpClientFactory Http { get; set; }

        public static ILogger Log { get; set; }

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
