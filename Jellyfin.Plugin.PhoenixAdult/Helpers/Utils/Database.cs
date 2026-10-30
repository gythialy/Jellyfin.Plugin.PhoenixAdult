using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PhoenixAdult.Helpers.Utils
{
    internal static class Database
    {
        private static readonly string DatabasePath = Path.Combine(Plugin.Instance.DataFolderPath, "data");

        public static SiteListStructure SiteList { get; set; }

        public static ActorsStructure Actors { get; set; }

        public static GenresStructure Genres { get; set; }

        public static async Task<bool> Download(string url, string fileName, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(DatabasePath))
            {
                Logger.Info($"Creating database directory \"{DatabasePath}\"");
                Directory.CreateDirectory(DatabasePath);
            }

            var encoding = new UTF8Encoding(false);
            var http = await HTTP.Request(url, cancellationToken).ConfigureAwait(false);
            if (http.IsOK)
            {
                Logger.Info($"Database file \"{fileName}\" downloaded successfully");
                File.WriteAllText(Path.Combine(DatabasePath, fileName), http.Content, encoding);

                return true;
            }

            return false;
        }

        public static void LoadAll()
        {
            if (Directory.Exists(DatabasePath))
            {
                foreach (var filePath in Directory.GetFiles(DatabasePath, "*.json"))
                {
                    var fileName = Path.GetFileName(filePath);

                    Load(fileName);
                }
            }

            MergeActorsLocal();
        }

        public static bool IsExist(string fileName)
        {
            return File.Exists(Path.Combine(DatabasePath, fileName));
        }

        /// <summary>
        /// 合并用户本地覆盖文件 Actors.local.json（该文件不会被 "Update Database" 任务覆盖）。
        /// 结构与 Actors.json 相同；与远端数据取并集：同名 key 追加别名，本地新增条目直接加入。
        /// </summary>
        private static void MergeActorsLocal()
        {
            var filePath = Path.Combine(DatabasePath, "Actors.local.json");
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                var encoding = new UTF8Encoding(false);
                var local = JsonConvert.DeserializeObject<ActorsStructure>(File.ReadAllText(filePath, encoding));
                var merged = Actors;

                if (local.ActorsReplace != null)
                {
                    merged.ActorsReplace ??= new Dictionary<string, string[]>();
                    foreach (var pair in local.ActorsReplace)
                    {
                        if (merged.ActorsReplace.TryGetValue(pair.Key, out var existing) && existing != null)
                        {
                            merged.ActorsReplace[pair.Key] = existing
                                .Union(pair.Value ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase)
                                .ToArray();
                        }
                        else
                        {
                            merged.ActorsReplace[pair.Key] = pair.Value;
                        }
                    }
                }

                if (local.ActorsStudioIndexes != null)
                {
                    merged.ActorsStudioIndexes ??= new Dictionary<int, string[]>();
                    foreach (var pair in local.ActorsStudioIndexes)
                    {
                        merged.ActorsStudioIndexes[pair.Key] = pair.Value;
                    }
                }

                if (local.ActorsReplaceStudios != null)
                {
                    merged.ActorsReplaceStudios ??= new Dictionary<int, Dictionary<string, string[]>>();
                    foreach (var site in local.ActorsReplaceStudios)
                    {
                        if (merged.ActorsReplaceStudios.TryGetValue(site.Key, out var existingStudios) && existingStudios != null)
                        {
                            foreach (var pair in site.Value)
                            {
                                if (existingStudios.TryGetValue(pair.Key, out var existing) && existing != null)
                                {
                                    existingStudios[pair.Key] = existing
                                        .Union(pair.Value ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase)
                                        .ToArray();
                                }
                                else
                                {
                                    existingStudios[pair.Key] = pair.Value;
                                }
                            }
                        }
                        else
                        {
                            merged.ActorsReplaceStudios[site.Key] = site.Value;
                        }
                    }
                }

                Actors = merged;
                Logger.Info("Actors.local.json merged");
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to merge Actors.local.json: \"{e.Message}\"");
            }
        }

        private static void Load(string fileName)
        {
            var encoding = new UTF8Encoding(false);
            var serializer = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.None,
            };

            var filePath = Path.Combine(DatabasePath, fileName);
            if (File.Exists(filePath))
            {
                var data = File.ReadAllText(filePath, encoding);
                switch (fileName)
                {
                    case "SiteList.json":
                        SiteList = JsonConvert.DeserializeObject<SiteListStructure>(data, serializer);
                        break;

                    case "Actors.json":
                        Actors = JsonConvert.DeserializeObject<ActorsStructure>(data, serializer);
                        break;

                    case "Genres.json":
                        Genres = JsonConvert.DeserializeObject<GenresStructure>(data, serializer);
                        break;

                    default:
                        break;
                }
            }
        }

        public struct SiteListStructure
        {
            public Dictionary<int, Dictionary<int, string[]>> Sites { get; set; }

            public Dictionary<int, string> SiteIDList { get; set; }

            public Dictionary<string, string> Abbrieviations { get; set; }
        }

        public struct ActorsStructure
        {
            public Dictionary<string, string[]> ActorsReplace { get; set; }

            public Dictionary<int, string[]> ActorsStudioIndexes { get; set; }

            public Dictionary<int, Dictionary<string, string[]>> ActorsReplaceStudios { get; set; }
        }

        public struct GenresStructure
        {
            public Dictionary<string, string[]> GenresReplace { get; set; }

            public Dictionary<string, string[]> GenresPartialReplace { get; set; }

            public Dictionary<string, string[]> GenresSplit { get; set; }

            public List<string> GenresSkip { get; set; }

            public List<string> GenresPartialSkip { get; set; }
        }
    }
}
