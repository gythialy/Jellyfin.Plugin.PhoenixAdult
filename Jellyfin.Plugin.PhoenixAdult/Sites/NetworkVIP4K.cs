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
    public class NetworkVIP4K : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> GenresDB = new Dictionary<string, List<string>>
        {
            { "Stuck 4k", new List<string> { "Stuck" } },
            { "Tutor 4k", new List<string> { "Tutor" } },
            { "Sis", new List<string> { "Step Sister" } },
            { "Shame 4k", new List<string> { "MILF" } },
            { "Mature 4k", new List<string> { "GILF" } },
            { "Pie 4K", new List<string> { "Creampie" } },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string sceneID = null;
            string sceneURL = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var id) && id > 10)
            {
                sceneID = parts[0];
                searchTitle = searchTitle.Replace(sceneID, string.Empty).Trim();
                sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/en/videos/{sceneID}";
            }

            if (!string.IsNullOrEmpty(sceneURL))
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);

                    var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                    var titleNoFormatting = titleNode != null ? Helper.ParseTitle(titleNode.InnerText.Split('|').Last().Trim(), siteNum) : string.Empty;

                    var siteNameNode = doc.DocumentNode.SelectSingleNode("//a[@class='player-additional__site ph_register']");
                    var subSite = siteNameNode != null ? siteNameNode.InnerText.Trim() : Helper.GetSearchSiteName(siteNum);

                    var curID = Helper.Encode(sceneURL);

                    var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='player-additional__text']");
                    var releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var posterNode = doc.DocumentNode.SelectSingleNode("//div[@class='player-item__block']//img");
                    var imageUrl = posterNode != null ? posterNode.GetAttributeValue("src", string.Empty) : string.Empty;
                    if (imageUrl.StartsWith("//", StringComparison.Ordinal))
                    {
                        imageUrl = $"https:{imageUrl}";
                    }

                    var score = 100;
                    if (searchDate.HasValue)
                    {
                        score -= LevenshteinDistance.Calculate(searchDate.Value.ToString("yyyy-MM-dd"), releaseDate);
                    }
                    else
                    {
                        score -= LevenshteinDistance.Calculate(searchTitle.ToLower(), titleNoFormatting.ToLower());
                    }

                    if (!subSite.Equals(Helper.GetSearchSiteName(siteNum), StringComparison.OrdinalIgnoreCase))
                    {
                        score--;
                    }

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}" } },
                        Name = titleNoFormatting,
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = imageUrl,
                    };

                    if (DateTime.TryParse(releaseDate, out var searchPremiereDate))
                    {
                        item.PremiereDate = searchPremiereDate;
                    }

                    result.Add(item);
                }
            }
            else
            {
                var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}";
                var http = await HTTP.Request(searchUrl, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);

                    foreach (var searchResultNode in doc.DocumentNode.SelectNodes("//div[@class='item__description']"))
                    {
                        var titleNode = searchResultNode.SelectSingleNode(".//a[@class='item__title']");
                        var titleNoFormatting = titleNode != null ? Helper.ParseTitle(titleNode.InnerText.Trim(), siteNum) : string.Empty;
                        sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}{titleNode.GetAttributeValue("href", string.Empty)}";

                        var siteNameNode = searchResultNode.SelectSingleNode(".//a[@class='item__site']");
                        var subSite = siteNameNode != null ? siteNameNode.InnerText.Trim() : Helper.GetSearchSiteName(siteNum);

                        var curID = Helper.Encode(sceneURL);
                        var releaseDate = searchDate.HasValue ? searchDate.Value.ToString("yyyy-MM-dd") : string.Empty;

                        var imageNode = searchResultNode.SelectSingleNode(
                            "ancestor::div[contains(concat(' ', normalize-space(@class), ' '), ' item ')]//div[@class='item__image']//img");
                        var imageUrl = imageNode != null ? imageNode.GetAttributeValue("src", string.Empty) : string.Empty;
                        if (imageUrl.StartsWith("//", StringComparison.Ordinal))
                        {
                            imageUrl = $"https:{imageUrl}";
                        }

                        var score = 100;
                        if (searchDate.HasValue)
                        {
                            score -= LevenshteinDistance.Calculate(searchDate.Value.ToString("yyyy-MM-dd"), releaseDate);
                        }
                        else
                        {
                            score -= LevenshteinDistance.Calculate(searchTitle.ToLower(), titleNoFormatting.ToLower());
                        }

                        if (!subSite.Equals(Helper.GetSearchSiteName(siteNum), StringComparison.OrdinalIgnoreCase))
                        {
                            score--;
                        }

                        var item = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}" } },
                            Name = titleNoFormatting,
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = imageUrl,
                        };

                        if (DateTime.TryParse(releaseDate, out var parsedDate))
                        {
                            item.PremiereDate = parsedDate;
                        }

                        result.Add(item);
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

            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            var sceneDate = providerIds[1];

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.ExternalId = sceneURL;

            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                movie.Name = Helper.ParseTitle(titleNode.InnerText.Split('|').Last().Trim(), siteNum);
            }

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='player-description__text']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("VIP4K");

            var taglineNode = doc.DocumentNode.SelectSingleNode("//a[@class='player-additional__site ph_register']");
            if (taglineNode != null)
            {
                var tagline = taglineNode.InnerText.Trim();
                movie.Tagline = tagline;
                movie.AddStudio(tagline);
            }

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='player-additional__text']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedSceneDate))
            {
                movie.PremiereDate = parsedSceneDate;
                movie.ProductionYear = parsedSceneDate.Year;
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//a[@class='player-description__model model ph_register']");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorNameNode = actorLink.SelectSingleNode(".//div[@class='model__name']");
                    if (actorNameNode != null)
                    {
                        var actorName = actorNameNode.InnerText.Trim();
                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            var genres = new List<string>();
            if (GenresDB.TryGetValue(movie.Tagline, out var dbGenres))
            {
                genres.AddRange(dbGenres);
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//div[@class='tags']/a");
            if (genreNodes != null)
            {
                foreach (var genreLink in genreNodes)
                {
                    var genreName = genreLink.InnerText.Replace("#", string.Empty).Trim();
                    genres.Add(genreName);
                }
            }

            foreach (var genre in genres.Distinct())
            {
                movie.AddGenre(genre);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var imageNodes = doc.DocumentNode.SelectNodes("//div[@class='player-item__block']//img");
                if (imageNodes != null)
                {
                    var urls = new List<string>();
                    foreach (var img in imageNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src", string.Empty);
                        if (imgUrl.StartsWith("//", StringComparison.Ordinal))
                        {
                            imgUrl = $"https:{imgUrl}";
                        }

                        urls.Add(imgUrl);
                    }

                    for (var i = 0; i < urls.Count; i++)
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = urls[i],
                            Type = i == 0 ? ImageType.Primary : ImageType.Backdrop,
                        });
                    }

                    if (urls.Count == 1 && !string.IsNullOrEmpty(urls[0]))
                    {
                        images.Add(new RemoteImageInfo
                        {
                            Url = urls[0],
                            Type = ImageType.Backdrop,
                        });
                    }
                }
            }

            return images;
        }
    }
}
