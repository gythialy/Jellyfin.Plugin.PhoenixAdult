using System.Collections.Generic;
using System.Reflection;
using PhoenixAdult.Helpers;

namespace PhoenixAdult
{
    public static class Consts
    {
        public const string DatabaseUpdateURL = "https://api.github.com/repos/gythialy/Jellyfin.Plugin.PhoenixAdult/contents/data";

        /// <summary>
        /// 需要 Referer 防盗链的图片主机 → 该主机要求的 Referer 值。
        /// 这些 CDN 对签名 URL 做 Referer 校验，缺少或错误的 Referer 会直接返回 403。
        /// 默认值来自 <see cref="ImageRefererDefaults"/>（同时会写进插件默认配置，可在设置页里改），
        /// 这里再派生一份用于运行时兜底：用户规则没命中时仍按内置表来。
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ImageReferers = BuildImageReferers();

        private static IReadOnlyDictionary<string, string> BuildImageReferers()
        {
            var rules = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var rule in ImageRefererDefaults.Rules)
            {
                if (ImageRefererDefaults.TryParse(rule, out var host, out var referer))
                {
                    rules[host] = referer;
                }
            }

            return rules;
        }

#if __EMBY__
        public const string PluginInstance = "Emby.Plugins.PhoenixAdult";
#else
        public const string SentryDSN = "https://8379d0e7cc2c45d8b1b6928ab8ff84c0@o1140949.ingest.sentry.io/6198587";

        public const string PluginInstance = "Jellyfin.Plugin.PhoenixAdult";
#endif

        public static readonly string PluginVersion = $"{Plugin.Instance.Version} build {Helper.GetLinkerTime(Assembly.GetAssembly(typeof(Plugin)))}";
    }
}
