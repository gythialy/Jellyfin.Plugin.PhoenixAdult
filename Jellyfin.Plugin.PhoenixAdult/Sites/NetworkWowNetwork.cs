using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkWowNetwork : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            // WordPress OR matching is unreliable, so search only the tail of the title:
            // the file name puts the title last, so take its last four words.
            searchTitle = Helper.GetSearchTitle(searchTitle, 4, fromEnd: true);

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);

            // wowgirlsblog.com sits behind a Cloudflare challenge,
            // so use FlareSolverr when configured.
            string searchHtml;
            if (FlareSolverr.IsConfigured)
            {
                searchHtml = await FlareSolverr.GetHtml(searchUrl, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(searchHtml))
                {
                    return result;
                }
            }
            else
            {
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    return result;
                }

                searchHtml = httpResult.Content;
            }

            var searchResults = HTML.ElementFromString(searchHtml);
            var pages = new List<string> { searchUrl };
            var pageNodes = searchResults.SelectNodes("//div[@class='pagination']/ul/li/a");
            if (pageNodes != null)
            {
                pages.AddRange(pageNodes.Select(p => p.GetAttributeValue("href", string.Empty)).Where(p => !string.IsNullOrEmpty(p)));
            }

            foreach (var page in pages)
            {
                if (string.IsNullOrEmpty(page) || !page.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Pagination is protected the same way as the first page.
                string pageHtml;
                if (FlareSolverr.IsConfigured)
                {
                    pageHtml = await FlareSolverr.GetHtml(page, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var pageHttp = await HTTP.Request(page, HttpMethod.Get, cancellationToken);
                    pageHtml = pageHttp.IsOK ? pageHttp.Content : null;
                }

                if (!string.IsNullOrEmpty(pageHtml))
                {
                    var pageResults = HTML.ElementFromString(pageHtml);
                    var searchNodes = pageResults.SelectNodes("//main//article[contains(@class,'thumb-block')]");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string siteName = Helper.GetSearchSiteName(siteNum);
                            var linkNode = node.SelectSingleNode(".//a");
                            if (linkNode == null)
                            {
                                continue;
                            }

                            string titleNoFormatting = linkNode.GetAttributeValue("title", string.Empty).Trim();
                            string curId = Helper.Encode(linkNode.GetAttributeValue("href", string.Empty));
                            var imgNode = node.SelectSingleNode(".//img");
                            string imgUrl = imgNode?.GetAttributeValue("data-src", string.Empty) ?? string.Empty;
                            if (string.IsNullOrEmpty(imgUrl) || imgUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                            {
                                imgUrl = imgNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;
                            }

                            string image = Helper.Encode(imgUrl);

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{image}" } },
                                Name = $"{titleNoFormatting} [{siteName}]",
                                ImageUrl = imgUrl,
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            // The scene page is protected too, so use FlareSolverr like Search,
            // otherwise the title, date and actors are all empty.
            string detailsHtml;
            if (FlareSolverr.IsConfigured)
            {
                detailsHtml = await FlareSolverr.GetHtml(sceneUrl, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                detailsHtml = httpResult.IsOK ? httpResult.Content : null;
            }

            if (string.IsNullOrEmpty(detailsHtml))
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(detailsHtml);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            var titleNode = detailsPageElements.SelectSingleNode("//h1[@class='entry-title']");
            if (titleNode != null)
            {
                movie.Name = titleNode.InnerText.Trim();
            }

            movie.AddStudio("WowNetwork");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            // Date: prefer the article:published_time meta.
            var dateNode = detailsPageElements.SelectSingleNode("//meta[@property='article:published_time']/@content");
            string dateText = dateNode?.GetAttributeValue("content", string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(dateText))
            {
                dateText = detailsPageElements.SelectSingleNode("//div[@id='video-date']")?.InnerText.Replace("Date:", string.Empty).Trim() ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(dateText) && DateTime.TryParse(dateText, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                // Otherwise use meta[itemprop=uploadDate].
                var uploadDate = detailsPageElements.SelectSingleText("//meta[@itemprop='uploadDate']/@content");
                var dateMatch = Regex.Match(uploadDate, @"(\d{4}-\d{2}-\d{2})");
                if (dateMatch.Success && DateTime.TryParseExact(dateMatch.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='tags-list']/a//i[@class='fa fa-folder-open']/..");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    string genreName = genre.InnerText.Replace("Movies", string.Empty).Trim();
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@id='video-actors']//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            if (sceneID == null || sceneID.Length == 0)
            {
                return images;
            }

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            // Fallback: the cover saved during Search.
            string fallbackImage = providerIds.Length > 1 ? Helper.Decode(providerIds[1]) : string.Empty;
            if (string.IsNullOrEmpty(fallbackImage) || fallbackImage.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                fallbackImage = string.Empty;
            }

            // The scene page is Cloudflare protected; use FlareSolverr like Search and Update.
            string sceneHtml;
            if (FlareSolverr.IsConfigured)
            {
                sceneHtml = await FlareSolverr.GetHtml(sceneUrl, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                sceneHtml = httpResult.IsOK ? httpResult.Content : null;
            }

            if (string.IsNullOrEmpty(sceneHtml))
            {
                // Scene page unavailable: use the cover saved during Search.
                if (!string.IsNullOrEmpty(fallbackImage))
                {
                    images.Add(new RemoteImageInfo { Url = fallbackImage, Type = ImageType.Primary });
                }

                return images;
            }

            var detailsPageElements = HTML.ElementFromString(sceneHtml);

            // NextGen gallery: the original image of an album post.
            var galleryLinks = detailsPageElements.SelectNodes("//div[contains(@class,'ngg-galleryoverview')]//a/@href");
            var galleryImages = new List<string>();
            if (galleryLinks != null)
            {
                foreach (var link in galleryLinks)
                {
                    string url = link.GetAttributeValue("href", string.Empty);
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        && url.Contains("/wp-content/gallery/", StringComparison.OrdinalIgnoreCase))
                    {
                        galleryImages.Add(url);
                    }
                }
            }

            if (galleryImages.Any())
            {
                images.Add(new RemoteImageInfo
                {
                    Url = galleryImages.First(),
                    Type = ImageType.Primary,
                });

                foreach (var url in galleryImages)
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = url,
                        Type = ImageType.Backdrop,
                    });
                }
            }
            else
            {
                // Movie post: og:image or the fp-splash data-src.
                string cover = string.Empty;
                var ogImage = detailsPageElements.SelectSingleNode("//meta[@property='og:image']/@content");
                if (ogImage != null)
                {
                    cover = ogImage.GetAttributeValue("content", string.Empty);
                }

                if (string.IsNullOrEmpty(cover))
                {
                    cover = detailsPageElements.SelectSingleNode("//img[contains(@class,'fp-splash')]")?.GetAttributeValue("data-src", string.Empty) ?? string.Empty;
                }

                if (string.IsNullOrEmpty(cover))
                {
                    cover = fallbackImage;
                }

                if (!string.IsNullOrEmpty(cover))
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = cover,
                        Type = ImageType.Primary,
                    });

                    images.Add(new RemoteImageInfo
                    {
                        Url = cover,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return images;
        }
    }
}
