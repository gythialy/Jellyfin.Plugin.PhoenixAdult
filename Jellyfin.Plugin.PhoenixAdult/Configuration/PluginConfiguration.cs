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

            // 默认值在插件启动时由 ImageRefererDefaults 补进配置（见 Plugin.EnsureImageRefererDefaults）：
            // XmlSerializer 反序列化 List 是追加而不是替换，构造里给默认值会让每次读写都多出一份重复项。
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

        /// <summary>
        /// JavDB 登录态 Cookie（浏览器登录 javdb.com 后整段粘贴，形如 "over18=1; _jdb_session=..."）。
        /// JavDB 的演员页与无码作品页要求登录，未配置时这些页面按「不可用」降级，不影响有码作品。
        /// </summary>
        public string JavDBCookie { get; set; }

        /// <summary>
        /// 演员头像源为空时的兜底：用其作品封面（JavDB 海报）充当人物照片。
        /// 默认开启；关掉后没有头像的演员就是「没有照片」，不再用封面顶替。
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
        /// 番号检索时并发查询多个站点（不勾选则只查路由命中的单一站点）。
        /// </summary>
        public bool MultiSourceSearch { get; set; }

        /// <summary>
        /// 多源检索的单站超时（秒），默认 30。
        /// </summary>
        public int MultiSourceSearchTimeoutSeconds { get; set; }

        /// <summary>
        /// 条目「⋯」菜单里是否显示「抽帧」入口（仅 Jellyfin；且只在启用了本插件的库里出现）。
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
