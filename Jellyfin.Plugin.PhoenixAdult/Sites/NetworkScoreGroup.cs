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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkScoreGroup : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out var id))
            {
                sceneId = id.ToString();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            var searchResults = new List<string>();
            if (sceneId != null)
            {
                string actorName = Regex.Replace(searchTitle, @"\s\d.*", string.Empty).Replace(' ', '-');
                searchResults.Add($"{Helper.GetSearchSearchURL(siteNum)}{actorName}/{sceneId}/");
            }

            var searchPageElements = await GetSearchFromForm(searchTitle, siteNum, cancellationToken);
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'compact video')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = Helper.ParseTitle(node.SelectSingleNode(".//a[contains(@class, 'title')]")?.InnerText.Trim());
                    string sceneUrl = node.SelectSingleNode(".//a[contains(@class, 'title')]")?.GetAttributeValue("href", string.Empty).Trim().Split('?')[0];
                    string curId = Helper.Encode(sceneUrl);
                    string actors = Helper.Encode(node.SelectSingleNode(".//small[@class='i-model']")?.InnerText);
                    string img = Helper.Encode(node.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty));

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}||{Helper.Encode(titleNoFormatting)}|{actors}|{img}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var googleResults = await Search.GetSearchResults(searchTitle, siteNum, cancellationToken);
            string urlId = Helper.GetSearchSearchURL(siteNum).Replace(Helper.GetSearchBaseURL(siteNum), string.Empty);
            searchResults.AddRange(googleResults.Where(u => u.Contains(urlId) && !u.Contains("?") && !result.Any(r => Helper.Decode(r.ProviderIds.FirstOrDefault().Value.Split('|')[0]) == u)));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
                    if (!string.IsNullOrEmpty(titleNoFormatting) && !titleNoFormatting.Contains("404") && !Regex.IsMatch(titleNoFormatting, "Latest.*Videos"))
                    {
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = string.Empty;
                        var dateNode = detailsPageElements.SelectSingleNode("//div[./span[contains(., 'Date:')]]//span[@class='value']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchSearchURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            string title = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim());
            if (string.IsNullOrEmpty(title))
            {
                var actors = detailsPageElements.SelectNodes("//div/span[@class='value']/a");
                if (actors?.Count > 1)
                {
                    title = string.Join(" and ", actors.Select(a => a.InnerText));
                }
                else if (actors?.Count == 1)
                {
                    title = actors[0].InnerText;
                }
            }

            if (Regex.IsMatch(title, "Latest.*Videos"))
            {
                title = Helper.Decode(providerIds[3]);
                var actors = Helper.Decode(providerIds[4]).Split(',');
                foreach (var actor in actors)
                {
                    result.People.Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
                }
            }

            movie.Name = title.Replace("Coming Soon:", string.Empty).Trim();

            var summaryXPaths = new[] { "//div[contains(@class, 'p-desc')]/text()", "//div[contains(@class, 'desc')]/text()" };
            foreach (var xpath in summaryXPaths)
            {
                var summaryNodes = detailsPageElements.SelectNodes(xpath);
                if (summaryNodes != null)
                {
                    movie.Overview = string.Join("\n", summaryNodes.Select(s => s.InnerText).Where(s => !string.IsNullOrEmpty(s) && s != " "));
                    break;
                }
            }

            movie.AddStudio("Score Group");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div/span[@class='value'][2]");
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

            var genreXPaths = new[] { "//div[@class='mb-3']/a", "//div[contains(@class, 'desc')]//a[contains(@href, 'tag') or contains(@href, 'category')]" };
            foreach (var xpath in genreXPaths)
            {
                var genreNodes = detailsPageElements.SelectNodes(xpath);
                if (genreNodes != null)
                {
                    foreach (var genre in genreNodes)
                    {
                        movie.AddGenre(genre.InnerText.Trim());
                    }
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div/span[@class='value']/a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    if (actorName.ToLower() != "extra")
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            if (siteNum[0] == 1344)
            {
                result.People.Add(new PersonInfo { Name = "Christy Marks", Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);

            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchSearchURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var match = Regex.Match(httpResult.Content, "posterImage: '(.*)'");
            if (match.Success)
            {
                images.Add(new RemoteImageInfo { Url = match.Groups[1].Value });
            }

            var xpaths = new[] {
                "//script[@type]", "//div[contains(@class, 'thumb')]/img", "//div[contains(@class, 'p-image')]/a/img",
                "//div[contains(@class, 'dl-opts')]/a/img", "//div[contains(@class, 'p-photos')]/div/div/a",
                "//div[contains(@class, 'gallery')]/div/div/a",
            };

            foreach (var xpath in xpaths)
            {
                var imageNodes = detailsPageElements.SelectNodes(xpath);
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        var posterMatch = Regex.Match(img.InnerText, "(?<=poster: ').*(?=')");
                        string imageUrl = posterMatch.Success ? posterMatch.Groups[1].Value : (img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("href", string.Empty));
                        if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.Contains("shared-bits") && !imageUrl.Contains("/join"))
                        {
                            if (!imageUrl.StartsWith("http"))
                            {
                                imageUrl = $"http:{imageUrl}";
                            }

                            images.Add(new RemoteImageInfo { Url = imageUrl });
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

        private async Task<HtmlNode> GetSearchFromForm(string query, int[] siteNum, CancellationToken cancellationToken)
        {
            var values = new Dictionary<string, string>
            {
                { "keywords", query },
                { "s_filters[type]", "videos" },
                { "s_filters[site]", "current" },
            };
            string searchUrl = $"{Helper.GetSearchBaseURL(siteNum)}/search-es";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Post, new FormUrlEncodedContent(values), cancellationToken);
            if (httpResult.IsOK)
            {
                return HTML.ElementFromString(httpResult.Content);
            }

            return null;
        }
    }
}
