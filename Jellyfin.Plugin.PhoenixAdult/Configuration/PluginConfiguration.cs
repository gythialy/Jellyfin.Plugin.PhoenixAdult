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
            this.ImageReferers = new List<string>();

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
        /// 追加/覆盖图片 Referer 防盗链规则，每项一条 "host=referer"（host 可只写域名后缀）。
        /// 内置规则见 <see cref="Consts.ImageReferers"/>，此项仅用于站点 CDN 变更时的免改版修正；
        /// referer 留空表示显式关闭该 host 的内置规则。
        /// </summary>
        /// <remarks>
        /// 必须用 List&lt;string&gt; 而不是 Dictionary：Jellyfin/Emby 的插件配置由
        /// System.Xml.Serialization.XmlSerializer 持久化为 XML，该序列化器在反射配置类型时
        /// 直接拒绝任何 IDictionary 成员（"Cannot serialize member ... because it implements IDictionary"），
        /// 会让整个配置的读取与保存都抛异常。
        /// </remarks>
        public List<string> ImageReferers { get; set; }

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
