using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace PhoenixAdult.Configuration
{
    public enum JAVActorNamingStyle
    {
        WesternStyle = 0,
        JapaneseStyle = 1,
    }

    public enum GenresSortingStyle
    {
        Alphabetical = 0,
        PositionsLast = 1,
    }

    public enum PreferedActorNameSource
    {
        LocalDatabase = 0,
        NoChange = 1,
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            this.DatabaseHash = string.Empty;
            this.TokenStorage = string.Empty;

            this.UID = Guid.NewGuid().ToString();
            this.DisableAnalytics = false;
            this.EnableDebug = false;

            this.DisableCaching = false;
            this.DisableSSLCheck = false;

            this.ProxyEnable = false;
            this.ProxyHost = string.Empty;
            this.ProxyPort = 0;
            this.ProxyLogin = string.Empty;
            this.ProxyPassword = string.Empty;

            this.FlareSolverrURL = "http://localhost:8191/";

            // Defaults are seeded at startup: XmlSerializer appends list items on deserialize,
            // so constructor defaults would duplicate on every read/write.
            this.ImageReferers = new List<string>();

            this.JavDBCookie = string.Empty;
            this.ActorPhotoCoverFallback = true;

            this.DefaultSiteName = string.Empty;
            this.UseFilePath = false;

            this.UseMetadataAPI = true;
            this.MetadataAPIToken = string.Empty;

            this.DisableActors = false;
            this.TagActors = true;
            this.TagActorValue = "XXX";
            this.DisableGenres = false;
            this.DisableImageValidation = false;
            this.DisableImageSize = false;
            this.DisableAutoIdentify = false;

            this.MultiSourceSearch = true;
            this.MultiSourceSearchTimeoutSeconds = 30;

            this.EnableFrameGrabMenu = true;

            this.JAVActorNamingStyle = JAVActorNamingStyle.WesternStyle;
            this.GenresSortingStyle = GenresSortingStyle.Alphabetical;
            this.PreferedActorNameSource = PreferedActorNameSource.LocalDatabase;
        }

        public string DatabaseHash { get; set; }

        public string TokenStorage { get; set; }

        public string UID { get; set; }

        public bool DisableAnalytics { get; set; }

        public bool EnableDebug { get; set; }

        public bool DisableCaching { get; set; }

        public bool DisableSSLCheck { get; set; }

        public bool ProxyEnable { get; set; }

        public string ProxyHost { get; set; }

        public int ProxyPort { get; set; }

        public string ProxyLogin { get; set; }

        public string ProxyPassword { get; set; }

        public string FlareSolverrURL { get; set; }

        /// <summary>
        /// Extra referer rules, one "host=referer" per line; an empty referer disables that host.
        /// </summary>
        /// <remarks>
        /// Must stay List&lt;string&gt;: XmlSerializer rejects IDictionary members,
        /// which would break loading and saving the whole configuration.
        /// </remarks>
        public List<string> ImageReferers { get; set; }

        /// <summary>
        /// JavDB session cookie; without it actor pages and uncensored titles are unavailable.
        /// </summary>
        public string JavDBCookie { get; set; }

        /// <summary>
        /// Use a work cover when an actor has no portrait. Default on.
        /// </summary>
        public bool ActorPhotoCoverFallback { get; set; }

        public string DefaultSiteName { get; set; }

        public bool UseFilePath { get; set; }

        public bool UseMetadataAPI { get; set; }

        public string MetadataAPIToken { get; set; }

        public bool DisableActors { get; set; }

        public bool TagActors { get; set; }

        public string TagActorValue { get; set; }

        public bool DisableGenres { get; set; }

        public bool DisableImageValidation { get; set; }

        public bool DisableImageSize { get; set; }

        public bool DisableAutoIdentify { get; set; }

        /// <summary>
        /// Query several sites in parallel for a code instead of the routed one only.
        /// </summary>
        public bool MultiSourceSearch { get; set; }

        /// <summary>
        /// Per-site timeout for a multi-source search, in seconds.
        /// </summary>
        public int MultiSourceSearchTimeoutSeconds { get; set; }

        /// <summary>
        /// Show the frame grab entry in the item menu (Jellyfin only, libraries using this plugin).
        /// </summary>
        public bool EnableFrameGrabMenu { get; set; }

        public JAVActorNamingStyle JAVActorNamingStyle { get; set; }

        public GenresSortingStyle GenresSortingStyle { get; set; }

        public PreferedActorNameSource PreferedActorNameSource { get; set; }

#if __EMBY__
#else
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required")]
        public static void ConfigurationChanged(object sender, BasePluginConfiguration e)
        {
            var configuration = Plugin.Instance.Configuration;

            configuration.DefaultSiteName = configuration.DefaultSiteName.Trim();
            configuration.FlareSolverrURL = configuration.FlareSolverrURL.Trim();
            configuration.MetadataAPIToken = configuration.MetadataAPIToken.Trim();

            configuration.ProxyHost = configuration.ProxyHost.Trim();
            configuration.ProxyLogin = configuration.ProxyLogin.Trim();
            configuration.ProxyPassword = configuration.ProxyPassword.Trim();

            Plugin.Instance.SaveConfiguration(configuration);
        }
#endif
    }
}
