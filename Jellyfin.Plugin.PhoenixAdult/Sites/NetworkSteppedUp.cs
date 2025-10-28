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
    public class NetworkSteppedUp : IProviderBase
    {
        private async Task<string> GetBuildId(string url, CancellationToken cancellationToken)
        {
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var modelPageElements = HTML.ElementFromString(httpResult.Content);
                var data = JObject.Parse(modelPageElements.SelectSingleNode("//script[@type='application/json']")?.InnerText);
                return data["buildId"].ToString();
            }

            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string encoded = searchTitle.Split(new[] { "and" }, StringSplitOptions.None)[0].Trim().Replace(' ', '-').ToLower();
            string modelPageUrl = $"{Helper.GetSearchBaseURL(siteNum)}/models/{encoded}";
            string buildId = await GetBuildId(modelPageUrl, cancellationToken);
            if (buildId == null)
            {
                return result;
            }

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}/{buildId}/models/{encoded}.json";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = JObject.Parse(httpResult.Content);
            var modelContents = searchResults.SelectToken("pageProps.model_contents");
            if (modelContents != null && modelContents.Type != JTokenType.Null)
            {
                foreach (var searchResult in modelContents)
                {
                    string titleNoFormatting = Helper.ParseTitle(searchResult["title"].ToString(), siteNum);
                    string curId = Helper.Encode(searchResult["slug"].ToString());
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(searchResult["publish_date"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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

            string[] providerIds = sceneID[0].Split('|');
            string slug = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds[1];

            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/scenes/{slug}";
            string buildId = await GetBuildId(sceneUrl, cancellationToken);
            if (buildId == null)
            {
                return result;
            }

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}/{buildId}/scenes/{slug}.json";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = JObject.Parse(httpResult.Content)["pageProps"]["content"];

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements["title"].ToString(), siteNum);
            movie.Overview = detailsPageElements["description"].ToString();
            movie.AddStudio("Stepped Up Media");

            string tagline = detailsPageElements["site"].ToString();
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genre in detailsPageElements["tags"])
            {
                movie.AddGenre(genre.ToString().Trim());
            }

            foreach (var actor in detailsPageElements["models_thumbs"])
            {
                string actorName = actor["name"].ToString().Trim();
                string actorPhotoUrl = actor["thumb"].ToString();
                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string slug = Helper.Decode(providerIds[0]);

            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/scenes/{slug}";
            string buildId = await GetBuildId(sceneUrl, cancellationToken);
            if (buildId == null)
            {
                return images;
            }

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}/{buildId}/scenes/{slug}.json";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = JObject.Parse(httpResult.Content)["pageProps"]["content"];

            if (detailsPageElements["trailer_screencap"] != null)
            {
                images.Add(new RemoteImageInfo { Url = detailsPageElements["trailer_screencap"].ToString() });
            }

            if (detailsPageElements["extra_thumbnails"] != null)
            {
                foreach (var image in detailsPageElements["extra_thumbnails"])
                {
                    images.Add(new RemoteImageInfo { Url = image.ToString() });
                }
            }

            if (detailsPageElements["thumbs"] != null)
            {
                foreach (var image in detailsPageElements["thumbs"])
                {
                    images.Add(new RemoteImageInfo { Url = image.ToString() });
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
