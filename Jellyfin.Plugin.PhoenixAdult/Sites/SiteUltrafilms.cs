using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SiteUltraFilms : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string encodedSearchTitle = Uri.EscapeDataString(searchTitle);

            // First pass: search with quotes for exact match
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}%22{encodedSearchTitle}%22";
            var httpResult = await HTTP.Request(searchUrl, cancellationToken).ConfigureAwait(false);
            if (httpResult.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(httpResult.Content);

                var searchResults = doc.DocumentNode.SelectNodes("//main//article[@data-video-uid]");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        var titleNode = searchResult.SelectSingleNode(".//a/@title");
                        string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty).Trim() ?? string.Empty;

                        var imageNode = searchResult.SelectSingleNode(".//img/@data-src");
                        string image = Helper.Encode(imageNode?.GetAttributeValue("data-src", string.Empty) ?? string.Empty);

                        var linkNode = searchResult.SelectSingleNode(".//a/@href");
                        string curID = Helper.Encode(linkNode?.GetAttributeValue("href", string.Empty) ?? string.Empty);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{image}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }

            // Second pass: search without quotes if no perfect match
            if (result.Count == 0)
            {
                searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedSearchTitle}";
                httpResult = await HTTP.Request(searchUrl, cancellationToken).ConfigureAwait(false);
                if (httpResult.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(httpResult.Content);

                    var searchResults = doc.DocumentNode.SelectNodes("//main//article[@data-video-uid]");
                    if (searchResults != null)
                    {
                        foreach (var searchResult in searchResults)
                        {
                            var titleNode = searchResult.SelectSingleNode(".//a/@title");
                            string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty).Trim() ?? string.Empty;

                            var imageNode = searchResult.SelectSingleNode(".//img/@data-src");
                            string image = Helper.Encode(imageNode?.GetAttributeValue("data-src", string.Empty) ?? string.Empty);

                            var linkNode = searchResult.SelectSingleNode(".//a/@href");
                            string curID = Helper.Encode(linkNode?.GetAttributeValue("href", string.Empty) ?? string.Empty);

                            // Only add if it's not already in the results from the first pass
                            if (!result.Any(r => Helper.Decode(r.ProviderIds[Plugin.Instance.Name].Split('|')[0]) == Helper.Decode(curID)))
                            {
                                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                                result.Add(new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{image}|{releaseDate}" } },
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
            var movie = (Movie)result.Item;

            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}{sceneURL}";
            }

            var httpResult = await HTTP.Request(sceneURL, cancellationToken).ConfigureAwait(false);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(httpResult.Content);

            // Title
            var titleNode = doc.DocumentNode.SelectSingleNode("//h1[@class='entry-title']");
            if (titleNode != null)
            {
                movie.Name = titleNode.InnerText.Trim();
            }

            // Summary
            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='video-description']//div[@class='desc ']/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            // Studio
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            // Genres
            var genreNodes = doc.DocumentNode.SelectNodes("//div[@class='tags-list']/a[.//i[@class='fa fa-folder-open']]");
            if (genreNodes != null)
            {
                foreach (var genreNode in genreNodes)
                {
                    string genreName = genreNode.InnerText.Replace("Movies", string.Empty).Trim();
                    movie.AddGenre(genreName);
                }
            }

            // Release Date
            var dateNode = doc.DocumentNode.SelectSingleNode("//meta[@property='article:published_time']/@content");
            if (dateNode != null)
            {
                if (DateTime.TryParse(dateNode.GetAttributeValue("content", string.Empty), out var releaseDate))
                {
                    movie.PremiereDate = releaseDate;
                    movie.ProductionYear = releaseDate.Year;
                }
            }
            else if (providerIds.Length > 3 && !string.IsNullOrEmpty(providerIds[3]))
            {
                if (DateTime.TryParse(providerIds[3], out var releaseDate))
                {
                    movie.PremiereDate = releaseDate;
                    movie.ProductionYear = releaseDate.Year;
                }
            }

            // Actors
            var actorNodes = doc.DocumentNode.SelectNodes("//div[@id='video-actors']//a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actorNode in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actorNode.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            result.HasMetadata = true;
            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            string[] providerIds = sceneID[0].Split('|');
            if (providerIds.Length > 2)
            {
                string imageUrl = Helper.Decode(providerIds[2]);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    result.Add(new RemoteImageInfo
                    {
                        Url = imageUrl,
                        Type = ImageType.Primary,
                    });
                }
            }

            return result;
        }
    }
}
