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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkUnzipVR : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum).Replace("www", "content") + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(url, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(httpResult.Content);
            var videos = json["data"]?["videos"] as JArray;
            if (videos != null)
            {
                foreach (var video in videos)
                {
                    string title = video["title"]?.ToString();
                    string slug = video["slug"]?.ToString();
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(slug))
                    {
                        continue;
                    }

                    string curID = Helper.Encode(slug);
                    var score = 100 - LevenshteinDistance.Calculate(searchTitle, title, StringComparison.OrdinalIgnoreCase);

                    if (title.Length > 29)
                    {
                        title = title.Substring(0, 29) + "...";
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}" } },
                        Name = $"{title} [{Helper.GetSearchSiteName(siteNum)}]",
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

            string sceneId = Helper.Decode(sceneID[0]);
            string basePath = Helper.GetSearchBaseURL(siteNum).Replace("www", "content");
            string sceneURL = $"{basePath}/api/content/v1/videos/{sceneId}";

            var httpResult = await HTTP.Request(sceneURL, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(httpResult.Content);
            var details = json["data"]?["item"];
            if (details == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = $"{Helper.GetSearchBaseURL(siteNum)}/video/{sceneId}";
            movie.Name = details["title"]?.ToString();
            movie.Overview = HTML.StripHtml(details["description"]?.ToString() ?? string.Empty);
            movie.AddStudio("Unzip VR");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);
            movie.AddCollection(tagline);

            if (long.TryParse(details["publishedAt"]?.ToString(), out var publishedAt))
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(publishedAt).UtcDateTime;
                movie.PremiereDate = date;
                movie.ProductionYear = date.Year;
            }

            var categories = details["categories"] as JArray;
            if (categories != null)
            {
                foreach (var cat in categories)
                {
                    string genreName = cat["name"]?.ToString();
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            var models = details["models"] as JArray;
            if (models != null)
            {
                foreach (var model in models)
                {
                    string actorName = model["title"]?.ToString();
                    string slug = model["slug"]?.ToString();
                    if (string.IsNullOrEmpty(actorName))
                    {
                        continue;
                    }

                    string actorPhotoURL = string.Empty;
                    var featImg = model["featuredImage"];
                    if (featImg != null && featImg["permalink"] != null)
                    {
                        actorPhotoURL = basePath + featImg["permalink"].ToString();
                    }
                    else if (!string.IsNullOrEmpty(slug))
                    {
                        try
                        {
                            string modelURL = $"{basePath}/api/content/v1/models/{slug}";
                            var modelHttp = await HTTP.Request(modelURL, cancellationToken);
                            if (modelHttp.IsOK)
                            {
                                var modelJson = JObject.Parse(modelHttp.Content);
                                var modelFeatImg = modelJson["data"]?["item"]?["featuredImage"];
                                if (modelFeatImg != null && modelFeatImg["permalink"] != null)
                                {
                                    actorPhotoURL = basePath + modelFeatImg["permalink"].ToString();
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneId = Helper.Decode(sceneID[0]);
            string basePath = Helper.GetSearchBaseURL(siteNum).Replace("www", "content");
            string sceneURL = $"{basePath}/api/content/v1/videos/{sceneId}";

            var httpResult = await HTTP.Request(sceneURL, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var json = JObject.Parse(httpResult.Content);
            var details = json["data"]?["item"];
            if (details != null)
            {
                var slider = details["sliderImage"];
                if (slider != null && slider["permalink"] != null)
                {
                    images.Add(new RemoteImageInfo { Url = basePath + slider["permalink"].ToString(), Type = ImageType.Primary });
                }

                var poster = details["poster"];
                if (poster != null && poster["permalink"] != null)
                {
                    images.Add(new RemoteImageInfo { Url = basePath + poster["permalink"].ToString(), Type = ImageType.Primary });
                }

                var gallery = details["galleryImages"] as JArray;
                if (gallery != null)
                {
                    foreach (var img in gallery)
                    {
                        if (img["permalink"] != null)
                        {
                            images.Add(new RemoteImageInfo { Url = basePath + img["permalink"].ToString(), Type = ImageType.Backdrop });
                        }
                    }
                }
            }

            return images;
        }
    }
}
