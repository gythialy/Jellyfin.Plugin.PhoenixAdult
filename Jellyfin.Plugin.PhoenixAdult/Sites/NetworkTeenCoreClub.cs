using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class NetworkTeenCoreClub : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? "", out var id))
            {
                sceneId = id.ToString();
                searchTitle = searchTitle.Replace(sceneId, "").Trim();
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}videos/browse/search/{Uri.EscapeDataString(searchTitle)}?page=1&sg=false&sort=release&video_type=scene&lang=en&site_id=10&genre=0&dach=false";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var data = JObject.Parse(httpResult.Content);
            if (data["videos"]?["data"] != null)
            {
                foreach (var searchResult in data["videos"]["data"])
                {
                    string titleNoFormatting = searchResult["title"]["en"].ToString();
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(searchResult["publication_date"].ToString(), out var parsedDate))
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");

                    string searchId = searchResult["id"].ToString();
                    string curId = Helper.Encode($"{Helper.GetSearchSearchURL(siteNum)}/videodetail/{searchId}");

                    var actors = searchResult["actors"].Select(a => a["name"].ToString());
                    string actorsList = string.Join(", ", actors);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} ({actorsList}) [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var data = JObject.Parse(httpResult.Content);
            var video = data["video"];

            var movie = (Movie)result.Item;
            movie.Name = video["title"]["en"].ToString();
            movie.Overview = video["description"]["en"].ToString();
            movie.AddStudio("Teen Core Club");

            string tagline = video["labels"][0]["name"].ToString().Replace(".com", "").Trim();
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            var actors = new List<string>();
            foreach (var actorData in video["actors"])
            {
                string actorName = actorData["name"].ToString();
                actors.Add(actorName);
                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            if (movie.Name.ToLower().StartsWith("bic_"))
            {
                if (actors.Count == 1) movie.Name = actors[0];
                else if (actors.Count == 2) movie.Name = string.Join(" & ", actors);
                else movie.Name = string.Join(", ", actors);
            }

            if (DateTime.TryParse(video["publication_date"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreData in video["genres"])
            {
                if(genreData["title"]?["en"] != null)
                    movie.AddGenre(genreData["title"]["en"].ToString());
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;

            var data = JObject.Parse(httpResult.Content);
            var video = data["video"];

            if (video["artwork"]?["small"] != null) images.Add(new RemoteImageInfo { Url = video["artwork"]["small"].ToString() });
            if (video["artwork"]?["large"] != null) images.Add(new RemoteImageInfo { Url = video["artwork"]["large"].ToString() });
            if (video["cover"]?["small"] != null) images.Add(new RemoteImageInfo { Url = video["cover"]["small"].ToString() });
            if (video["cover"]?["medium"] != null) images.Add(new RemoteImageInfo { Url = video["cover"]["medium"].ToString() });
            if (video["cover"]?["large"] != null) images.Add(new RemoteImageInfo { Url = video["cover"]["large"].ToString() });

            if (video["screenshots"] != null)
            {
                foreach(var screenshot in video["screenshots"])
                    images.Add(new RemoteImageInfo { Url = screenshot.ToString() });
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
