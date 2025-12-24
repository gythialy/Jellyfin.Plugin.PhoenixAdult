using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class NetworkGASM : IProviderBase
    {
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "WarningModal", "true" } };
        private static readonly Dictionary<string, List<string>> channelIdDB = new Dictionary<string, List<string>>
        {
            { "23", new List<string> { "harmony vision" } }, { "102", new List<string> { "butt formation" } }, { "105", new List<string> { "pure xxx films" } },
            { "8110", new List<string> { "cosplay babes" } }, { "8111", new List<string> { "filthy and fisting" } }, { "8112", new List<string> { "fun movies" } },
            { "8113", new List<string> { "herzog" } }, { "8114", new List<string> { "hot gold" } }, { "8115", new List<string> { "inflagranti" } },
            { "8116", new List<string> { "japanhd" } }, { "8117", new List<string> { "Leche69" } }, { "8118", new List<string> { "magma film" } },
            { "8119", new List<string> { "mmv films" } }, { "8120", new List<string> { "paradise films" } }, { "8121", new List<string> { "pornxn" } },
            { "8122", new List<string> { "the undercover lover" } },
        };

        private string Slugify(string phrase)
        {
            string str = phrase.ToLowerInvariant();
            str = System.Text.Encoding.ASCII.GetString(System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(str));
            str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s-]", string.Empty);
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim();
            str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s", "+");
            return str;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out _))
            {
                sceneId = searchTitle.Split(' ').First();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            if (sceneId != null)
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/post/details/{sceneId}";
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                if (httpResult.IsOK)
                {
                    var searchResult = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = searchResult.SelectSingleNode("//h1[@class='post_title']/span")?.InnerText;
                    string curId = Helper.Encode(sceneUrl);
                    string subSite = Helper.ParseTitle(searchResult.SelectSingleNode("//a[contains(@href, '/studio/profile/')]")?.InnerText.Split(':').Last().Trim(), siteNum);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{subSite}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                string searchUrl = Helper.GetSearchSearchURL(siteNum) + Slugify(searchTitle);
                var siteName = Helper.GetSearchSiteName(siteNum).ToLower();
                if (siteNum[1] >= 1 && siteNum[1] <= 16)
                {
                    searchUrl = $"{searchUrl}&channel={channelIdDB.FirstOrDefault(kvp => kvp.Value.Contains(siteName)).Key}";
                }

                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                if (httpResult.IsOK)
                {
                    var searchResults = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchResults.SelectNodes("//div[contains(@class, 'results_item')]");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string titleNoFormatting = node.SelectSingleNode(".//a[@class='post_title']")?.InnerText;
                            string curId = Helper.Encode(node.SelectSingleNode(".//a[@class='post_title']")?.GetAttributeValue("href", string.Empty));
                            string subSite = node.SelectSingleNode(".//a[@class='post_channel']")?.InnerText.Split(':').Last().Trim();
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}" } },
                                Name = $"{titleNoFormatting} [{subSite}]",
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

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[@class='post_title']/span")?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode("//h2[@class='post_description']")?.InnerText.Replace("´", "'").Replace("’", "'");
            movie.AddStudio("GASM");

            string tagline = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//a[contains(@href, '/studio/profile/')]")?.InnerText.Trim(), siteNum);
            movie.AddStudio(tagline);

            var dvd = detailsPageElements.SelectSingleNode("//div[@class='post_item dvd']/h1");
            if (dvd != null)
            {
                movie.AddCollection(Helper.ParseTitle(dvd.InnerText.ToLower(), siteNum));
            }

            var dateNode = detailsPageElements.SelectSingleNode("//h3[@class='post_date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[contains(@href, '/search?s=')]");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[contains(@href, 'models/')]");
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
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//img[@class='item_cover'] | //meta[@name='twitter:image']");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("content", string.Empty) });
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
