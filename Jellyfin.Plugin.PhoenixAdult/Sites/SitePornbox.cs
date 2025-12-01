using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SitePornbox : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var sourceID = searchTitle.Split(' ').First();
            var searchData = searchTitle;

            if (int.TryParse(sourceID, out _))
            {
                searchData = searchTitle.Replace(sourceID, string.Empty).Trim();
                var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/contents/{sourceID}";
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var json = JObject.Parse(http.Content);
                    var titleNoFormatting = Helper.ParseTitle((string)json["scene_name"], siteNum);
                    var curID = Helper.Encode(sceneURL);
                    var releaseDate = (string)json["publish_date"];
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [Pornbox] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchData;
            var searchHttp = await HTTP.Request(searchURL, cancellationToken);
            if (searchHttp.IsOK)
            {
                var json = JObject.Parse(searchHttp.Content);
                foreach (var searchResult in json["content"]["contents"])
                {
                    var titleNoFormatting = Helper.ParseTitle((string)searchResult["scene_name"], siteNum);
                    var match = Regex.Match(titleNoFormatting, @"\w+\d$");
                    if (match.Success)
                    {
                        var matchID = match.Value;
                        titleNoFormatting = Regex.Replace(titleNoFormatting, @"\w+\d$", string.Empty).Trim();
                        titleNoFormatting = $"[{matchID}] {titleNoFormatting}";
                    }

                    var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/contents/{(string)searchResult["content_id"]}";
                    var curID = Helper.Encode(sceneURL);
                    var releaseDate = (string)searchResult["publish_date"];

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [Pornbox]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = Helper.ParseTitle((string)json["scene_name"], siteNum);
            movie.Overview = (string)json["small_description"];
            movie.AddStudio("Pornbox");

            var tagline = Helper.ParseTitle((string)json["studio"], siteNum);
            movie.AddStudio(tagline);

            if (DateTime.TryParse((string)json["publish_date"], out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genre in json["niches"])
            {
                movie.AddGenre((string)genre["niche"]);
            }

            var actors = new List<JToken>();
            if (json["models"] != null)
            {
                actors.AddRange(json["models"]);
            }

            if (json["male_models"] != null)
            {
                actors.AddRange(json["male_models"]);
            }

            foreach (var actor in actors)
            {
                var actorName = (string)actor["model_name"];
                var actorPageURL = $"{Helper.GetSearchBaseURL(siteNum)}/model/info/{(string)actor["model_id"]}";
                var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                if (actorHttp.IsOK)
                {
                    var actorJson = JObject.Parse(actorHttp.Content);
                    var actorPhotoURL = (string)actorJson["headshot"];
                    result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            if (tagline == "Giorgio Grandi" || tagline.Contains("Giorgio's Lab"))
            {
                result.AddPerson(new PersonInfo { Name = "Giorgio Grandi", Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var json = JObject.Parse(http.Content);
                images.Add(new RemoteImageInfo { Url = (string)json["player_poster"], Type = ImageType.Primary });

                var screenshots = json["screenshots"].Children().ToList();
                var step = screenshots.Count > 50 ? 10 : 1;
                for (var i = 0; i < screenshots.Count; i += step)
                {
                    images.Add(new RemoteImageInfo { Url = (string)screenshots[i]["xga_size"], Type = ImageType.Backdrop });
                }
            }

            return images;
        }
    }
}
