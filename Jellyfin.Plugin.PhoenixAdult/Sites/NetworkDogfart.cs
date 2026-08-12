using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkDogfart : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string apiKEY = await NetworkGammaEnt.GetAPIKey(siteNum, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(apiKEY))
            {
                return result;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
            var searchParams = $"query={searchTitle.Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase)}";
            var searchData = await NetworkGammaEnt.GetDataFromAPI(url, "all_scenes", Helper.GetSearchBaseURL(siteNum), searchParams, cancellationToken).ConfigureAwait(false);
            if (searchData == null)
            {
                return result;
            }

            foreach (JObject searchResult in searchData["results"].First["hits"])
            {
                string sceneID = (string)searchResult["clip_id"];
                var res = new RemoteSearchResult
                {
                    Name = (string)searchResult["title"],
                };

                if (searchResult["release_date"] != null && DateTime.TryParse((string)searchResult["release_date"], out var sceneDateObj))
                {
                    res.PremiereDate = sceneDateObj;
                }

                var curID = $"{sceneID}#scenes";
                if (res.PremiereDate.HasValue)
                {
                    curID += $"#{res.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
                }

                res.ProviderIds.Add(Plugin.Instance.Name, curID);

                if (searchResult.ContainsKey("pictures"))
                {
                    var image = (string)searchResult["pictures"].Last(o => !o.ToString().Equals("resized", StringComparison.OrdinalIgnoreCase));
                    res.ImageUrl = $"https://images-fame.gammacdn.com/movies/{image}";
                }

                result.Add(res);
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

            if (sceneID == null)
            {
                return result;
            }

            string apiKEY = await NetworkGammaEnt.GetAPIKey(siteNum, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(apiKEY))
            {
                return result;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
            var sceneData = await NetworkGammaEnt.GetDataFromAPI(url, "all_scenes", Helper.GetSearchBaseURL(siteNum), $"filters=clip_id={sceneID[0]}", cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            sceneData = (JObject)sceneData["results"].First["hits"].First;

            var movie = (Movie)result.Item;
            movie.ExternalId = Helper.GetSearchBaseURL(siteNum) + $"/en/video/0/{sceneID[0]}/";
            movie.Name = (string)sceneData["title"];

            var description = (string)sceneData["description"];
            if (!string.IsNullOrEmpty(description))
            {
                movie.Overview = description.Replace("</br>", "\n", StringComparison.OrdinalIgnoreCase);
            }

            var network = (string)sceneData["network_name"];
            if (!string.IsNullOrEmpty(network))
            {
                movie.AddStudio(network);
            }

            var studioName = (string)sceneData["studio_name"];
            if (!string.IsNullOrEmpty(studioName))
            {
                movie.AddStudio(studioName);
            }

            if (sceneID.Length > 2 && DateTime.TryParseExact(sceneID[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            if (sceneData["categories"] != null)
            {
                foreach (var genreLink in sceneData["categories"])
                {
                    var genreName = (string)genreLink["name"];
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            if (sceneData["actors"] != null)
            {
                foreach (var actorLink in sceneData["actors"])
                {
                    var actorName = (string)actorLink["name"];
                    if (!string.IsNullOrEmpty(actorName))
                    {
                        result.AddPerson(new PersonInfo { Name = actorName });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            string apiKEY = await NetworkGammaEnt.GetAPIKey(siteNum, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(apiKEY))
            {
                return result;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
            var sceneData = await NetworkGammaEnt.GetDataFromAPI(url, "all_scenes", Helper.GetSearchBaseURL(siteNum), $"filters=clip_id={sceneID[0]}", cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            sceneData = (JObject)sceneData["results"].First["hits"].First;

            if (sceneData.ContainsKey("pictures"))
            {
                var image = (string)sceneData["pictures"].Last(o => !o.ToString().Equals("resized", StringComparison.OrdinalIgnoreCase));
                var imageURL = $"https://images-fame.gammacdn.com/movies/{image}";

                result.Add(new RemoteImageInfo
                {
                    Url = imageURL,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = imageURL,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
