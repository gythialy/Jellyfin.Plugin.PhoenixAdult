using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = HTML.ElementFromString(httpResult.Content);
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

                var pageHttp = await HTTP.Request(page, HttpMethod.Get, cancellationToken);
                if (pageHttp.IsOK)
                {
                    var pageResults = HTML.ElementFromString(pageHttp.Content);
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

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

            // 日期：优先 article:published_time meta（video-date 元素在新版页面已移除）
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

            // 兜底：Search 阶段保存的封面（data-src 真图）
            string fallbackImage = providerIds.Length > 1 ? Helper.Decode(providerIds[1]) : string.Empty;
            if (string.IsNullOrEmpty(fallbackImage) || fallbackImage.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                fallbackImage = string.Empty;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                if (!string.IsNullOrEmpty(fallbackImage))
                {
                    images.Add(new RemoteImageInfo { Url = fallbackImage, Type = ImageType.Primary });
                }

                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            // NextGen gallery：相册 post 的原图（1200x675 ~ 1800x1201 高清）
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
                // movie post：og:image 或 fp-splash data-src
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
