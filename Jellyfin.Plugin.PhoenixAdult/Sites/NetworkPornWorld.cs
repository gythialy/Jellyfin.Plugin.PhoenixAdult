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
    public class NetworkPornWorld : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (searchDate.HasValue)
            {
                var delta = DateTime.Today - searchDate.Value;
                int searchPage = Math.Max((int)Math.Ceiling((double)delta.Days / 99), 1);
                string searchName = searchTitle.ToLower().Split(' ')[0];
                string searchUrl = $"https://www.pornworld.com/new-videos/{searchPage}";

                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchResults = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchResults.SelectNodes("//div[contains(concat(' ',normalize-space(@class),' '),' card-scene ')]");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string titleNoFormatting = node.SelectSingleNode(".//div[@class='card-scene__text']/a")?.InnerText.Trim();
                            string sceneDateStr = node.SelectSingleNode(".//div[@class=\"label label--time\"][2]")?.InnerText.Trim();
                            if (DateTime.TryParse(sceneDateStr, out var sceneDate))
                            {
                                if (Math.Abs((searchDate.Value - sceneDate).Days) < 3)
                                {
                                    string url = node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty);
                                    string curId = Helper.Encode(url);
                                    result.Add(new RemoteSearchResult
                                    {
                                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {sceneDateStr}",
                                        SearchProviderName = Plugin.Instance.Name,
                                    });
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                string sceneId = null;
                if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out var id) && id > 1000)
                {
                    sceneId = id.ToString();
                    searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
                }

                if (sceneId != null)
                {
                    string url = $"{Helper.GetSearchBaseURL(siteNum)}/watch/{sceneId}";
                    var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                        string curId = Helper.Encode(url);
                        string titleNoFormatting = GetTitle(detailsPageElements, siteNum);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = titleNoFormatting,
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
                else
                {
                    string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(' ', '+');
                    var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var searchResults = HTML.ElementFromString(httpResult.Content);
                        var searchNodes = searchResults.SelectNodes("//div[@class='card-scene__text']");
                        if (searchNodes != null)
                        {
                            foreach (var node in searchNodes)
                            {
                                string titleNoFormatting = node.SelectSingleNode("./a")?.InnerText.Trim();
                                string url = node.SelectSingleNode("./a")?.GetAttributeValue("href", string.Empty);
                                string curId = Helper.Encode(url);
                                result.Add(new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                                    SearchProviderName = Plugin.Instance.Name,
                                });
                            }
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
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
            movie.Name = GetTitle(detailsPageElements, siteNum);

            var descriptionNode = detailsPageElements.SelectSingleNode("//div[text()='Description:']/following-sibling::div");
            if (descriptionNode != null)
            {
                movie.Overview = descriptionNode.InnerText.Trim();
            }

            movie.AddStudio("PornWorld");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//i[contains(@class, 'bi-calendar')]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'genres-list')]//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//h1[contains(@class, 'watch__title')]//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//video");
            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("data-poster", string.Empty), Type = ImageType.Primary });
            }

            return images;
        }

        private string GetTitle(HtmlNode detailsPageElements, int[] siteNum)
        {
            string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//title")?.InnerText.Trim(), siteNum);
            return Regex.Replace(titleNoFormatting, @" - PornWorld$", string.Empty);
        }
    }
}
