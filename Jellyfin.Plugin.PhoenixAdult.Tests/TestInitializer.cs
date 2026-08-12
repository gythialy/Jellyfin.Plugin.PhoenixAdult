using System.Text;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhoenixAdult;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 测试环境初始化：mock Jellyfin 依赖，构造 Plugin 实例，
/// 供在线冒烟测试使用（离线数据测试不依赖此初始化）。
/// </summary>
public static class TestInitializer
{
    private static bool initialized;

    /// <summary>
    /// 初始化 global::PhoenixAdult.Plugin.Instance / Plugin.Http / Plugin.Log，
    /// 加载 data/ 下的 JSON 数据库。线程安全，只执行一次。
    /// </summary>
    public static void Init()
    {
        // .NET Core 默认不含 codepages，站点实现里用到 "Cyrillic" 等编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (initialized)
        {
            return;
        }

        lock (typeof(TestInitializer))
        {
            if (initialized)
            {
                return;
            }

            var repoRoot = TestPaths.RepoRoot;
            var dataDir = TestPaths.DataDir;

            var appPaths = new MockAppPaths(repoRoot);
            var serializer = new MockXmlSerializer();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddHttpClient();
            using var sp = services.BuildServiceProvider();

            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<global::PhoenixAdult.Plugin>>();

            _ = new global::PhoenixAdult.Plugin(appPaths, serializer, httpFactory, logger);

            // 让 HTTP 类的静态构造读取 global::PhoenixAdult.Plugin.Instance.Configuration
            // （通过触发一个无害访问）
            if (global::PhoenixAdult.Plugin.Instance == null)
            {
                throw new InvalidOperationException("global::PhoenixAdult.Plugin.Instance 初始化失败");
            }

            // 直接加载本地 data 目录的 JSON（不依赖 DataFolderPath 下下载）
            LoadDatabaseDirect(dataDir);

            initialized = true;
        }
    }

    private static void LoadDatabaseDirect(string dataDir)
    {
        // Database 是 internal static，通过反射加载
        var dbType = typeof(global::PhoenixAdult.Helpers.Utils.Database);
        var siteListProp = dbType.GetProperty("SiteList", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var actorsProp = dbType.GetProperty("Actors", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var genresProp = dbType.GetProperty("Genres", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        var serializer = new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None };

        var siteList = Newtonsoft.Json.JsonConvert.DeserializeObject<global::PhoenixAdult.Helpers.Utils.Database.SiteListStructure>(
            File.ReadAllText(Path.Combine(dataDir, "SiteList.json")), serializer);
        siteListProp!.SetValue(null, siteList);

        var actors = Newtonsoft.Json.JsonConvert.DeserializeObject<global::PhoenixAdult.Helpers.Utils.Database.ActorsStructure>(
            File.ReadAllText(Path.Combine(dataDir, "Actors.json")), serializer);
        actorsProp!.SetValue(null, actors);

        var genres = Newtonsoft.Json.JsonConvert.DeserializeObject<global::PhoenixAdult.Helpers.Utils.Database.GenresStructure>(
            File.ReadAllText(Path.Combine(dataDir, "Genres.json")), serializer);
        genresProp!.SetValue(null, genres);
    }

    private sealed class MockAppPaths : IApplicationPaths
    {
        private readonly string root;

        public MockAppPaths(string root)
        {
            this.root = root;
        }

        public string ProgramDataPath => this.root;
        public string WebPath => Path.Combine(this.root, "web");
        public string ProgramSystemPath => this.root;
        public string DataPath => this.root;
        public string ImageCachePath => Path.Combine(this.root, "cache", "images");
        public string PluginsPath => Path.Combine(this.root, "plugins");
        public string PluginConfigurationsPath => Path.Combine(this.root, "config");
        public string LogDirectoryPath => Path.Combine(this.root, "log");
        public string ConfigurationDirectoryPath => Path.Combine(this.root, "config");
        public string SystemConfigurationFilePath => Path.Combine(this.root, "config", "system.xml");
        public string CachePath => Path.Combine(this.root, "cache");
        public string TempDirectory => Path.Combine(this.root, "tmp");
        public string VirtualDataPath => this.root;
        public string TrickplayPath => Path.Combine(this.root, "trickplay");
        public string BackupPath => Path.Combine(this.root, "backup");

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string directoryPath, string fileName, bool deleteIfExists)
        {
        }
    }

    private sealed class MockXmlSerializer : IXmlSerializer
    {
        public bool CanDeserialize(Stream stream, Type type) => false;

        public object DeserializeFromFile(Type type, string file) => Activator.CreateInstance(type)!;

        public object DeserializeFromStream(Type type, Stream stream) => Activator.CreateInstance(type)!;

        public object DeserializeFromBytes(Type type, byte[] buffer) => Activator.CreateInstance(type)!;

        public void SerializeToFile(object obj, string file)
        {
        }

        public void SerializeToStream(object obj, Stream stream)
        {
        }
    }
}
