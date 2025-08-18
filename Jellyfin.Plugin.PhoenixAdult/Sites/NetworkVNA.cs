using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
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
    public class NetworkVNA : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var sceneId = searchTitle.Split(' ').First();
            if (!int.TryParse(sceneId, out _))
            {
                sceneId = null;
            }

            var searchResults = new List<string>();
            if (!string.IsNullOrEmpty(sceneId))
            {
                var directURL = $"{Helper.GetSearchSearchURL(siteNum)}{sceneId}";
                searchResults.Add(directURL);
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                if (sceneURL.Contains("/videos/") && !sceneURL.Contains("/page/") && !searchResults.Contains(sceneURL))
                {
                    searchResults.Add(sceneURL);
                }
            }

            foreach (var sceneURL in searchResults)
            {
                var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
                if (doc != null)
                {
                    var titleNode = doc.SelectSingleNode("//h1[@class='customhcolor']");
                    var titleNoFormatting = titleNode?.InnerText.Trim();

                    var curID = Helper.Encode(sceneURL);

                    var dateNode = doc.SelectSingleNode("//*[@class='date']");
                    var releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var score = 100;
                    if (searchDate.HasValue && !string.IsNullOrEmpty(releaseDate))
                    {
                        score -= LevenshteinDistance.Calculate(searchDate.Value.ToString("yyyy-MM-dd"), releaseDate);
                    }
                    else
                    {
                        score -= LevenshteinDistance.Calculate(searchTitle.ToLower(), titleNoFormatting.ToLower());
                    }

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                        Name = $"[{Helper.GetSearchSiteName(siteNum)}] {titleNoFormatting} {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    };
                    result.Add(item);
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
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            var siteNumVal = int.Parse(providerIds[1]);

            var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (doc == null)
            {
                return result;
            }

            movie.Name = doc.SelectSingleNode("//h1[@class='customhcolor']")?.InnerText.Trim();
            movie.Overview = doc.SelectSingleNode("//*[@class='customhcolor2']")?.InnerText.Trim();

            if (siteNumVal == 1287 && !string.IsNullOrEmpty(movie.Overview))
            {
                movie.Overview = movie.Overview.Split(new[] { "Don't forget to join me" }, StringSplitOptions.None)[0];
            }

            movie.AddStudio("VNA Network");
            var tagline = Helper.GetSearchSiteName(new[] { siteNumVal });
            movie.Tagline = tagline;
            movie.AddTag(tagline);

            var dateNode = doc.SelectSingleNode("//*[@class='date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genresNode = doc.SelectSingleNode("//h4[@class='customhcolor']");
            if (genresNode != null)
            {
                foreach (var genreLink in genresNode.InnerText.Trim().Split(','))
                {
                    movie.AddGenre(genreLink.Trim());
                }
            }

            var actorsNode = doc.SelectSingleNode("//h3[@class='customhcolor']");
            if (actorsNode != null)
            {
                var actors = actorsNode.InnerText.Trim();
                if (siteNumVal == 1288)
                {
                    movie.Overview = movie.Overview.Replace(actors, string.Empty).Trim();
                    if (genresNode != null)
                    {
                        actors = actors.Replace(genresNode.InnerText.Trim(), string.Empty);
                    }
                }

                foreach (var actorLink in actors.Replace("&nbsp", string.Empty).Split(','))
                {
                    var actorName = actorLink.Trim();
                    if (actorName.EndsWith(" XXX"))
                    {
                        actorName = actorName.Substring(0, actorName.Length - 4);
                    }

                    if (!string.IsNullOrEmpty(actorName))
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            if (siteNumVal == 1314)
            {
                result.People.Add(new PersonInfo { Name = "Siri", Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);

            var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (doc != null)
            {
                var imageNodes = doc.SelectNodes("//center//img/@src");
                if (imageNodes != null)
                {
                    var baseUri = new Uri(Helper.GetSearchBaseURL(siteNum));
                    foreach (var img in imageNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src", string.Empty);
                        if (!imgUrl.StartsWith("http"))
                        {
                            imgUrl = new Uri(baseUri, imgUrl).ToString();
                        }

                        images.Add(new RemoteImageInfo { Url = imgUrl });
                    }

                    if (images.Count > 0 && images[0].Url.Contains("thumb_1"))
                    {
                        images.Add(new RemoteImageInfo { Url = images[0].Url.Replace("thumb_1", "thumb_2") });
                        images.Add(new RemoteImageInfo { Url = images[0].Url.Replace("thumb_1", "thumb_3") });
                    }

                    for (int i = 0; i < images.Count; i++)
                    {
                        images[i].Type = i == 0 ? ImageType.Primary : ImageType.Backdrop;
                    }
                }
            }

            return images;
        }
    }
}
