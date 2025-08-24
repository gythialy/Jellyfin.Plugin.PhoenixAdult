using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json;
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
    public class NetworkStrike3 : IProviderBase
    {
        private const string SearchQuery = @"query getSearchResults($query:String!,$site:Site!,$first:Int,$skip:Int){searchVideos(input:{query:$query,site:$site,first:$first,skip:$skip}){edges{node{videoId title releaseDate slug images{listing{src}}}}}}";
        private const string UpdateQuery = @"query getSearchResults($slug:String!,$site:Site!){findOneVideo(input:{slug:$slug,site:$site}){videoId title description releaseDate models{name slug images{listing{highdpi{double}}}}directors{name}categories{name}carousel{listing{highdpi{triple}}}}}";
        private const string SearchIdQuery = @"query getSearchResults($videoId:ID!,$site:Site!){findOneVideo(input:{videoId:$videoId,site:$site}){videoId title releaseDate slug}}";

        private async Task<JObject> GetDataFromAPI(string url, string query, string variables, string referer, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Referer", referer },
            };

            var payload = new
            {
                query,
                variables = JObject.Parse(variables),
            };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var param = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var http = await HTTP.Request(url, HttpMethod.Post, param, headers, null, cancellationToken);
            return http.IsOK ? (JObject)JObject.Parse(http.Content)["data"] : null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string url = Helper.GetSearchSearchURL(siteNum);
            string siteName = Helper.GetSearchSiteName(siteNum).ToUpper();

            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var sceneID) && parts[0].Length > 4)
            {
                var variables = JsonConvert.SerializeObject(new { videoId = sceneID, site = siteName });
                var searchResult = await GetDataFromAPI(url, SearchIdQuery, variables, Helper.GetSearchBaseURL(siteNum), cancellationToken);
                if (searchResult?["findOneVideo"] != null)
                {
                    var video = searchResult["findOneVideo"];
                    string titleNoFormatting = (string)video["title"];
                    string releaseDate = DateTime.Parse((string)video["releaseDate"]).ToString("yyyy-MM-dd");
                    string curID = Helper.Encode((string)video["slug"]);
                    int videoID = (int)video["videoId"];

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                var variables = JsonConvert.SerializeObject(new { query = searchTitle, site = siteName, first = 10, skip = 0 });
                var searchResults = await GetDataFromAPI(url, SearchQuery, variables, Helper.GetSearchBaseURL(siteNum), cancellationToken);
                if (searchResults?["searchVideos"]?["edges"] != null)
                {
                    foreach (var searchResult in searchResults["searchVideos"]["edges"])
                    {
                        var node = searchResult["node"];
                        string titleNoFormatting = (string)node["title"];
                        string releaseDate = DateTime.Parse((string)node["releaseDate"]).ToString("yyyy-MM-dd");
                        string curID = Helper.Encode((string)node["slug"]);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{titleNoFormatting} {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = (string)node["images"]?["listing"]?.FirstOrDefault()?["src"],
                        });
                    }
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

            string sceneURL = Helper.Decode(sceneID[0]);
            var variables = JsonConvert.SerializeObject(new { slug = sceneURL, site = Helper.GetSearchSiteName(siteNum).ToUpper() });
            var url = Helper.GetSearchSearchURL(siteNum);
            var sceneData = await GetDataFromAPI(url, UpdateQuery, variables, Helper.GetSearchBaseURL(siteNum), cancellationToken);
            if (sceneData?["findOneVideo"] == null)
            {
                return result;
            }

            var video = (JObject)sceneData["findOneVideo"];
            var movie = (Movie)result.Item;

            movie.Name = (string)video["title"];
            movie.Overview = (string)video["description"];
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            if (DateTime.TryParse((string)video["releaseDate"], out var releaseDate))
            {
                movie.PremiereDate = releaseDate;
                movie.ProductionYear = releaseDate.Year;
            }

            string studioName = Helper.GetSearchSiteName(siteNum);
            if (studioName.Equals("Tushy", StringComparison.OrdinalIgnoreCase) || studioName.Equals("TushyRaw", StringComparison.OrdinalIgnoreCase))
            {
                movie.AddGenre("Anal");
            }

            if (video["categories"] != null)
            {
                foreach (var genreLink in video["categories"])
                {
                    movie.AddGenre((string)genreLink["name"]);
                }
            }

            if (video["models"] != null)
            {
                foreach (var actorLink in video["models"])
                {
                    result.People.Add(new PersonInfo
                    {
                        Name = (string)actorLink["name"],
                        ImageUrl = (string)actorLink["images"]?["listing"]?.FirstOrDefault()?["highdpi"]?["double"],
                        Type = PersonKind.Actor,
                    });
                }
            }

            if (video["directors"]?.Any() == true)
            {
                result.People.Add(new PersonInfo { Name = (string)video["directors"][0]["name"], Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            var variables = JsonConvert.SerializeObject(new { slug = sceneURL, site = Helper.GetSearchSiteName(siteNum).ToUpper() });
            var url = Helper.GetSearchSearchURL(siteNum);
            var sceneData = await GetDataFromAPI(url, UpdateQuery, variables, Helper.GetSearchBaseURL(siteNum), cancellationToken);
            if (sceneData?["findOneVideo"] == null)
            {
                return result;
            }

            var video = (JObject)sceneData["findOneVideo"];

            string posterUrl = null;
            if (video["images"]?["movie"]?.Any() == true)
            {
                posterUrl = (string)video["images"]["movie"].Last?["highdpi"]?["3x"] ?? (string)video["images"]["movie"].Last?["src"];
            }
            else if (video["images"]?["poster"]?.Any() == true)
            {
                posterUrl = (string)video["images"]["poster"].Last?["highdpi"]?["3x"] ?? (string)video["images"]["poster"].Last?["src"];
            }

            if (!string.IsNullOrEmpty(posterUrl))
            {
                result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
            }

            if (video["carousel"] != null)
            {
                foreach (var image in video["carousel"])
                {
                    string img = (string)image["listing"]?.FirstOrDefault()?["highdpi"]?["triple"];
                    if(!string.IsNullOrEmpty(img))
                    {
                        result.Add(new RemoteImageInfo { Url = img, Type = ImageType.Backdrop });
                    }
                }
            }

            return result;
        }
    }
}
