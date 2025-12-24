using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteAussieAss : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> actorsDB = new Dictionary<string, List<string>>
        {
            { "Belinda Belfast", new List<string> { "belinda belfast" } },
            { "Charlotte Star", new List<string> { "charlotte,star" } },
            { "Charlie Brookes", new List<string> { "charlie, brookes", "charlie" } },
            { "Monte Cooper", new List<string> { "monte, cooper", "monte cooper" } },
        };

        private string Slugify(string phrase)
        {
            string str = phrase.ToLowerInvariant();

            // invalid chars
            str = Regex.Replace(str, @"[^a-z0-9\s-]", string.Empty);

            // convert multiple spaces into one space
            str = Regex.Replace(str, @"\s+", " ").Trim();

            // cut and trim
            str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
            str = Regex.Replace(str, @"\s", "-"); // hyphens
            return str;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out _))
            {
                sceneId = searchTitle.Split(' ').First();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim().Replace("'", string.Empty);
            }

            if (sceneId != null)
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/webmasters/{sceneId.TrimStart('0')}";
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchResults = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = Regex.Replace(searchResults.SelectSingleNode("//h1|//h4/span")?.InnerText.Trim() ?? string.Empty, @"^\d+", string.Empty).Trim().ToLower();
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneUrl) } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                string encoded = Regex.Match(searchTitle, @"^\S*.\S*").Value.Replace(" ", string.Empty).ToLower();
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encoded}.html";
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchResults = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchResults.SelectNodes("//div[@class='infos']");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string resultTitleId = node.SelectSingleNode(".//span[@class='video-title']")?.InnerText.Trim();
                            string titleNoFormatting = Regex.Replace(resultTitleId, @"^\d+", string.Empty).ToLower();
                            string curId = Helper.Encode(node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty));
                            string releaseDate = string.Empty;
                            var dateNode = node.SelectSingleNode(".//span[@class='video-date']");
                            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
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
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            string resultTitleId;
            if (sceneUrl.Contains("webmasters"))
            {
                resultTitleId = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            }
            else
            {
                resultTitleId = detailsPageElements.SelectSingleNode("//h4/span")?.InnerText.Trim();
            }

            movie.Name = Regex.Replace(resultTitleId, @"^\d+", string.Empty).Trim().ToLower();

            if (sceneUrl.Contains("webmasters"))
            {
                movie.Overview = detailsPageElements.SelectNodes("//div[@class='row gallery-description']//div").LastOrDefault()?.InnerText.Trim();
            }
            else
            {
                movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='row']//a")?.GetAttributeValue("title", string.Empty).Trim();
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var xpaths = new[]
            {
                "//img[contains(@alt, 'content')]/@src",
                "//div[@class='box']//img/@src",
            };

            foreach (var xpath in xpaths)
            {
                var imageNodes = detailsPageElements.SelectNodes(xpath);
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        string imageUrl = img.GetAttributeValue("src", string.Empty);
                        if (!imageUrl.StartsWith("http"))
                        {
                            if (imageUrl.Contains("join"))
                            {
                                continue;
                            }
                            else if (sceneUrl.Contains("webmasters"))
                            {
                                imageUrl = $"{sceneUrl}/{imageUrl}";
                            }
                            else
                            {
                                imageUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{imageUrl}";
                            }
                        }

                        images.Add(new RemoteImageInfo { Url = imageUrl });
                    }
                }
            }

            if (!sceneUrl.Contains("webmasters"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(item.Name, @"\d+");
                if (match.Success)
                {
                    string altUrl = $"{Helper.GetSearchBaseURL(siteNum)}/webmasters/{match.Value.TrimStart('0')}";
                    var altHttp = await HTTP.Request(altUrl, HttpMethod.Get, cancellationToken);
                    if (altHttp.IsOK)
                    {
                        var altPage = HTML.ElementFromString(altHttp.Content);
                        var imageNodes = altPage.SelectNodes(xpaths[0]);
                        if (imageNodes != null)
                        {
                            foreach (var img in imageNodes)
                            {
                                string imageUrl = img.GetAttributeValue("src", string.Empty);
                                if (!imageUrl.StartsWith("http"))
                                {
                                    imageUrl = $"{altUrl}/{imageUrl}";
                                }

                                images.Add(new RemoteImageInfo { Url = imageUrl });
                            }
                        }
                    }
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
