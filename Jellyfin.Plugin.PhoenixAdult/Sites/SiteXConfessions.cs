using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteXConfessions : IProviderBase
    {
        private async Task<string> GetToken(int[] siteNum, CancellationToken cancellationToken)
        {
            var url = Helper.GetSearchBaseURL(siteNum);
            if (url.Contains("//api."))
            {
                url = url.Replace("//api.", "//");
            }

            if (url.Contains("//next-prod-api."))
            {
                url = url.Replace("//next-prod-api.", "//");
            }

            var httpResult = await HTTP.Request(url, cancellationToken);
            if (httpResult.IsOK)
            {
                var match = Regex.Match(httpResult.Content, @"\.access_token=\""(.*?)\""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        private async Task<JToken> GetDatafromAPI(int[] siteNum, string searchData, string token, CancellationToken cancellationToken, bool search = true)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {token}" },
            };

            if (search)
            {
                var searchUrl = Helper.GetSearchSearchURL(siteNum);
                var payload = new { query = searchData };
                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Post, content, cancellationToken, headers: headers);
                if (httpResult.IsOK)
                {
                    return JObject.Parse(httpResult.Content);
                }
            }
            else
            {
                var url = $"{Helper.GetSearchBaseURL(siteNum)}/api/movies/slug/{searchData}";
                var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken, headers: headers);
                if (httpResult.IsOK)
                {
                    var data = JObject.Parse(httpResult.Content);
                    return data["data"];
                }
            }

            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var token = await GetToken(siteNum, cancellationToken);
            if (token != null)
            {
                var searchResults = await GetDatafromAPI(siteNum, searchTitle, token, cancellationToken);
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        if ((string)searchResult["resourceType"] == "movies")
                        {
                            var curId = (string)searchResult["slug"];
                            var titleNoFormatting = (string)searchResult["title"];

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curId } },
                                Name = titleNoFormatting,
                                SearchProviderName = Plugin.Instance.Name,
                                ImageUrl = (string)(searchResult["poster_picture"] ?? string.Empty),
                            });
                        }
                    }
                }

                var slug = searchTitle.Replace(" ", "-").ToLower();
                var directMatch = await GetDatafromAPI(siteNum, slug, token, cancellationToken, false);
                if (directMatch != null)
                {
                    var curId = (string)directMatch["slug"];
                    var titleNoFormatting = (string)directMatch["title"];
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = titleNoFormatting,
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = (string)(directMatch["poster_picture"] ?? string.Empty),
                    });
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };
            var movie = (Movie)result.Item;

            var token = await GetToken(siteNum, cancellationToken);
            if (token != null)
            {
                var detailsPageElements = await GetDatafromAPI(siteNum, sceneID[0], token, cancellationToken, false);
                if (detailsPageElements != null)
                {
                    movie.Name = (string)detailsPageElements["title"];
                    movie.Overview = (string)detailsPageElements["synopsis_clean"];
                    var producer = detailsPageElements["producer"];
                    movie.AddStudio($"{(string)producer["name"]} {(string)producer["last_name"]}");

                    var producerPhotoUrl = (string)producer["poster_image"] ?? string.Empty;
                    result.People.Add(new PersonInfo { Name = $"{(string)producer["name"]} {(string)producer["last_name"]}", Type = PersonKind.Producer, ImageUrl = producerPhotoUrl.Split('?')[0] });

                    var tagline = Helper.GetSearchSiteName(siteNum);
                    movie.AddTag(tagline);
                    movie.AddCollection(tagline);

                    if (DateTime.TryParse((string)detailsPageElements["release_date"], out var releaseDate))
                    {
                        movie.PremiereDate = releaseDate;
                        movie.ProductionYear = releaseDate.Year;
                    }

                    foreach (var genre in detailsPageElements["tags"])
                    {
                        movie.AddGenre((string)genre["title"]);
                    }

                    if ((bool?)detailsPageElements["is_compilation"] == true || movie.Name.ToLower().Contains("compilation") || movie.Overview.ToLower().Contains("compilation"))
                    {
                        movie.AddGenre("Compilation");
                    }

                    if (double.TryParse((string)detailsPageElements["rating"], out var rating))
                    {
                        movie.CommunityRating = (float)(rating * 2);
                    }

                    foreach (var actor in detailsPageElements["performers"])
                    {
                        var actorPhotoUrl = (string)actor["poster_image"] ?? string.Empty;
                        result.People.Add(new PersonInfo { Name = $"{(string)actor["name"]} {(string)actor["last_name"]}", Type = PersonKind.Actor, ImageUrl = actorPhotoUrl.Split('?')[0] });
                    }

                    var director = detailsPageElements["director"];
                    result.People.Add(new PersonInfo { Name = $"{(string)director["name"]} {(string)director["last_name"]}", Type = PersonKind.Director });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var token = await GetToken(siteNum, cancellationToken);
            if (token != null)
            {
                var detailsPageElements = await GetDatafromAPI(siteNum, sceneID[0], token, cancellationToken, false);
                if (detailsPageElements != null)
                {
                    if (detailsPageElements["poster_picture"] != null)
                    {
                        images.Add(new RemoteImageInfo { Url = ((string)detailsPageElements["poster_picture"]).Split('?')[0], Type = ImageType.Primary });
                    }
                    else if (detailsPageElements["banner_image_mobile"] != null)
                    {
                        images.Add(new RemoteImageInfo { Url = ((string)detailsPageElements["banner_image_mobile"]).Split('?')[0], Type = ImageType.Primary });
                    }

                    foreach (var photo in detailsPageElements["album"])
                    {
                        images.Add(new RemoteImageInfo { Url = ((string)photo["path"]).Split('?')[0], Type = ImageType.Backdrop });
                    }
                }
            }

            return images;
        }
    }
}
