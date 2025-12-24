using System.Reflection;
using PhoenixAdult.Helpers;

namespace PhoenixAdult
{
    public static class Consts
    {
        public const string DatabaseUpdateURL = "https://api.github.com/repos/Andrer757/Jellyfin.Plugin.PhoenixAdult/contents/data";

        public const string SentryDSN = "https://8379d0e7cc2c45d8b1b6928ab8ff84c0@o1140949.ingest.sentry.io/6198587";

        public const string PluginInstance = "Jellyfin.Plugin.PhoenixAdult";

        public static readonly string PluginVersion = $"{Plugin.Instance.Version} build {Helper.GetLinkerTime(Assembly.GetAssembly(typeof(Plugin)))}";
    }
}
