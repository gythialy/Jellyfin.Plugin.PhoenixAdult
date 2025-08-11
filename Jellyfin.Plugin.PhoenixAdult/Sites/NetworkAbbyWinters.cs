using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Jellyfin.Data.Enums;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkAbbyWinters : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle)) return result;

            var searchResults = new HashSet<string>();
            var modelResults = await HTML.ElementFromURL($"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}", cancellationToken);
            if(modelResults != null && int.Parse(modelResults.SelectSingleNode("//span[@id='browse-total-count']")?.InnerText.Trim() ?? "0") != 0)
            {
                foreach (var modelURL in modelResults.SelectNodes("//div[@id='browse-grid']/main/article//a[@class]/@href"))
                {
                    var modelPageResults = await HTML.ElementFromURL(modelURL.GetAttributeValue("href", ""), cancellationToken);
                    if(modelPageResults != null)
                    {
                        foreach(var sceneURL in modelPageResults.SelectNodes("//div[@id='subject-shoots']//h2//@href"))
                        {
                            var url = sceneURL.GetAttributeValue("href", "");
                            if (!url.Contains("/nude_girl/") && !url.Contains("/shoots/") && !url.Contains("/fetish/") && !url.Contains("/updates/"))
                                searchResults.Add(url);
                        }
                    }
                }
            }

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var url = sceneURL.Replace("/cn/", "/").Replace("/de/", "/").Replace("/jp/", "/").Replace("/ja/", "/").Replace("/en/", "/");
                if (!url.Contains("/nude_girl/") && !url.Contains("/shoots/") && !url.Contains("/fetish/") && !url.Contains("/updates/"))
                    searchResults.Add(url);
            }

            string prevModelURL = string.Empty;
            HtmlNode actorPageElements = null;
            foreach (var sceneURL in searchResults)
            {
                var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
                if (detailsPageElements == null) continue;

                string titleNoFormatting = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split(':').Last().Split('|')[0].Trim();
                string subSite = detailsPageElements.SelectSingleNode("//div[@id='shoot-featured-image']//h4")?.InnerText.Trim();
                string curID = Helper.Encode(sceneURL);
                string modelURL = detailsPageElements.SelectSingleNode("//tr[contains(., 'Scene')]//a")?.GetAttributeValue("href", "");

                if (modelURL != prevModelURL)
                {
                    actorPageElements = await HTML.ElementFromURL(modelURL, cancellationToken);
                    prevModelURL = modelURL;
                }

                string date = string.Empty;
                if (actorPageElements != null)
                {
                    foreach (var scene in actorPageElements.SelectNodes("//article[@class='card card-shoot']"))
                    {
                        if (titleNoFormatting.Equals(scene.SelectSingleNode(".//h2")?.InnerText.Trim(), StringComparison.OrdinalIgnoreCase) && subSite.Equals(scene.SelectSingleNode(".//h3/text()")?.InnerText.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            date = scene.SelectSingleNode(".//span")?.InnerText.Trim();
                            break;
                        }
                    }
                }

                string releaseDate = !string.IsNullOrEmpty(date) ? DateTime.Parse(date).ToString("yyyy-MM-dd") : (searchDate.HasValue ? searchDate.Value.ToString("yyyy-MM-dd") : string.Empty);

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } },
                    Name = $"{titleNoFormatting} [Abby Winters/{subSite}] {releaseDate}",
                    SearchProviderName = Plugin.Instance.Name
                });
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
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split(':').Last().Split('|')[0].Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//aside/div[contains(@class, 'description')]")?.InnerText.Replace('\n', ' ').Trim();
            movie.AddStudio("Abby Winters");

            string tagline = detailsPageElements.SelectSingleNode("//div[@id='shoot-featured-image']//h4")?.InnerText.Trim();
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//aside/div[contains(@class, 'description')]//a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//tr[contains(., 'Scene')]//a");
            if (actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string modelURL = actor.GetAttributeValue("href", "");
                    var actorPageElements = await HTML.ElementFromURL(modelURL, cancellationToken);
                    string actorPhotoURL = actorPageElements?.SelectSingleNode("//img[@class='img-responsive']")?.GetAttributeValue("src", "");
                    result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var xpaths = new[] { "//div[contains(@class, 'tile-image')]/img/@src", "//div[contains(@class, 'video')]/@data-poster" };
            foreach(var xpath in xpaths)
            {
                var imageNodes = detailsPageElements.SelectNodes(xpath);
                if (imageNodes != null)
                {
                    foreach(var image in imageNodes)
                    {
                        result.Add(new RemoteImageInfo { Url = image.GetAttributeValue(xpath.Split('@').Last(), "") });
                    }
                }
            }

            if (result.Any())
            {
                result.First().Type = ImageType.Primary;
                foreach(var image in result.Skip(1))
                    image.Type = ImageType.Backdrop;
            }

            return result;
        }
    }
}
