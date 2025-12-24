using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.ScheduledTasks
{
    public class UpdateDatabase : IScheduledTask
    {
        public string Key => Plugin.Instance.Name + "UpdateDatabase";

        public string Name => "Update Database";

        public string Description => "Update database with sites / genres / actors";

        public string Category => Plugin.Instance.Name;

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            Logger.Info("Start Database Sync Update");
            await Task.Yield();
            progress?.Report(0);

            var db = new JObject();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.DatabaseHash))
            {
                db = JObject.Parse(Plugin.Instance.Configuration.DatabaseHash);
            }

            var data = await HTTP.Request(Consts.DatabaseUpdateURL, cancellationToken).ConfigureAwait(false);
            Logger.Info($"data: {data}");
            if (data.IsOK)
            {
                var json = JArray.Parse(data.Content);

                for (var i = 0; i < json.Count; i++)
                {
                    var file = json[i];

                    var url = (string)file["download_url"];
                    var fileName = (string)file["name"];
                    var sha = (string)file["sha"];
                    var type = (string)file["type"];
                    Logger.Info($"filename: {fileName}");
                    Logger.Info($"url: {url}");

                    progress?.Report((double)i / json.Count * 100);

                    if (type == "file" && (!db.ContainsKey(fileName) || (string)db[fileName] != sha || !Database.IsExist(fileName)))
                    {
                        Logger.Info($"filename: {fileName}");
                        Logger.Info($"url: {url}");
                        if (await Database.Download(url, fileName, cancellationToken).ConfigureAwait(false))
                        {
                            if (db.ContainsKey(fileName))
                            {
                                db[fileName] = sha;
                            }
                            else
                            {
                                db.Add(fileName, sha);
                            }
                        }
                    }
                }
            }

            Database.LoadAll();

            Plugin.Instance.Configuration.DatabaseHash = JsonConvert.SerializeObject(db);
            Plugin.Instance.SaveConfiguration();

            progress?.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };

            yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks };
        }
    }
}
