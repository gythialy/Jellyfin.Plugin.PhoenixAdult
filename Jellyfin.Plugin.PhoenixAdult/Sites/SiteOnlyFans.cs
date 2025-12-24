using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteOnlyFans : IProviderBase
    {
        private const string FilenameRegex = @"^OnlyFans \((.*?)\) - (\d{4}-\d{2}-\d{2}) - (.*)$";

        public Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            Logger.Info($"[OnlyFans] Searching for: '{searchTitle}'");
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                Logger.Info("[OnlyFans] Search title is empty.");
                return Task.FromResult(result);
            }

            var match = Regex.Match(searchTitle, FilenameRegex);
            Logger.Info($"OF match.Success: {match.Success}");
            if (match.Success)
            {
                string actorName = match.Groups[1].Value.Trim();
                string date = match.Groups[2].Value.Trim();
                string sceneName = match.Groups[3].Value.Trim();

                Logger.Info($"[OnlyFans] Match success. Actor: {actorName}, Date: {date}, Scene: {sceneName}");

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(searchTitle) } }, // Use the full filename as the ID
                    Name = sceneName,
                    SearchProviderName = Plugin.Instance.Name,
                };

                if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    res.PremiereDate = sceneDateObj;
                }

                result.Add(res);
            }
            else
            {
                Logger.Info($"[OnlyFans] Filename regex failed for title: '{searchTitle}'. Expected format: 'OnlyFans (Actor) - YYYY-MM-DD - Title'");
            }

            return Task.FromResult(result);
        }

        public Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            if (sceneID == null || sceneID.Length == 0)
            {
                Logger.Info("[OnlyFans] Update called with empty sceneID.");
                return Task.FromResult(result);
            }

            string filename = Helper.Decode(sceneID[0]);
            Logger.Info($"[OnlyFans] Updating metadata for: {filename}");
            var match = Regex.Match(filename, FilenameRegex);

            if (match.Success)
            {
                string[] actorNames = match.Groups[1].Value.Split(',').Select(a => a.Trim()).ToArray();
                string date = match.Groups[2].Value.Trim();
                string sceneName = match.Groups[3].Value.Trim();

                var movie = (Movie)result.Item;
                movie.Name = sceneName;
                movie.AddStudio("OnlyFans");

                if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    movie.PremiereDate = sceneDateObj;
                    movie.ProductionYear = sceneDateObj.Year;
                }

                foreach (var actorName in actorNames)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }
            else
            {
                Logger.Error($"[OnlyFans] Update regex failed for filename: {filename}");
            }

            result.HasMetadata = true;
            return Task.FromResult(result);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            // No images to fetch from filename
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(new List<RemoteImageInfo>());
        }
    }
}
