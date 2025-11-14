using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class NetworkAdultEmpireCash : IProviderBase
    {
        private readonly Dictionary<string, string> cookies = new Dictionary<string, string> { { "ageConfirmed", "true" } };

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
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            if (sceneId != null)
            {
                string directUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{sceneId}/{this.Slugify(searchTitle)}.html";
                var directHttp = await HTTP.Request(directUrl, HttpMethod.Get, cancellationToken, null, cookies: this.cookies);
                if (directHttp.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(directHttp.Content);
                    var titleNode = detailsPageElements.SelectSingleNode("//h1[@class='description']");
                    string titleNoFormatting = Helper.ParseTitle(titleNode?.InnerText.Trim(), siteNum);
                    string curId = Helper.Encode(directUrl);

                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='release-date']");
                    if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }
                    else if (searchDate.HasValue)
                    {
                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, null, cookies: this.cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            Logger.Info($"[NetworkAdultEmpireCash] Raw HTML: {httpResult.Content}");
            HtmlNodeCollection searchResultNodes;
            if (siteNum[1] == 10)
            {
                searchResultNodes = HTML.ElementFromString(httpResult.Content).SelectNodes("//div[starts-with(@id, 'ascene_')]");
            }
            else
            {
                searchResultNodes = HTML.ElementFromString(httpResult.Content).SelectNodes("//div[contains(@class, 'item-grid')]/div[@class='grid-item']");
            }

            if (searchResultNodes == null)
            {
                Logger.Info($"[NetworkAdultEmpireCash] results: null");
                return result;
            }

            Logger.Info($"[NetworkAdultEmpireCash] results: {searchResultNodes.Count}");
            foreach (var searchResultNode in searchResultNodes)
            {
                try
                {
                    string titleNoFormatting = string.Empty;
                    string curId = string.Empty;
                    var siteNumVal = siteNum[1];
                    if (siteNumVal == 0 || siteNumVal == 1 || siteNumVal == 14 || siteNumVal == 33)
                    {
                        titleNoFormatting = Helper.ParseTitle(searchResultNode.SelectSingleNode(".//img[contains(@class, 'img-full-fluid')]")?.GetAttributeValue("title", string.Empty).Trim(), siteNum);
                        curId = Helper.Encode(searchResultNode.SelectSingleNode(".//article[contains(@class, 'scene-update')]/a")?.GetAttributeValue("href", string.Empty));
                    }
                    else if (siteNumVal == 4 || siteNumVal == 17 || siteNumVal == 26 || siteNumVal == 28)
                    {
                        titleNoFormatting = Helper.ParseTitle(searchResultNode.SelectSingleNode(".//a[@class='scene-title']/p")?.InnerText.Split(new[] { " | " }, StringSplitOptions.None)[0].Trim(), siteNum);
                        curId = Helper.Encode(searchResultNode.SelectSingleNode(".//a[@class='scene-title']")?.GetAttributeValue("href", string.Empty));
                    }
                    else if (siteNumVal == 10)
                    {
                        titleNoFormatting = Helper.ParseTitle(searchResultNode.SelectSingleNode(".//a[contains(@class, 'scene-title')]/h6")?.InnerText.Trim(), siteNum);
                        curId = Helper.Encode(searchResultNode.SelectSingleNode(".//a[contains(@class, 'scene-title')]")?.GetAttributeValue("href", string.Empty));
                    }
                    else
                    {
                        titleNoFormatting = Helper.ParseTitle(searchResultNode.SelectSingleNode(".//a[@class='scene-title']/h6")?.InnerText.Trim(), siteNum);
                        curId = Helper.Encode(searchResultNode.SelectSingleNode(".//a[@class='scene-title']")?.GetAttributeValue("href", string.Empty));
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
                catch
                {
                    // ignore
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken, cookies: this.cookies);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;

            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[@class='description']")?.InnerText.Trim(), siteNum);

            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='synopsis']/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("Adult Empire Cash");
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            var taglineNode = detailsPageElements.SelectSingleNode("//div[@class='studio']//span/text()[2]");
            if (taglineNode != null)
            {
                string tagline = taglineNode.InnerText.Trim();
                movie.AddTag(tagline);
                movie.AddCollection(tagline);
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='release-date']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='tags']//a");
            if (genreNodes != null)
            {
                foreach (var genreNode in genreNodes)
                {
                    movie.AddGenre(genreNode.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='video-performer']//img");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.GetAttributeValue("title", string.Empty);
                    string actorPhotoUrl = actorNode.GetAttributeValue("data-bgsrc", string.Empty);
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            var actorNodes2 = detailsPageElements.SelectNodes("//div[contains(@class, 'video-performer-container')][2]/a");
            if (actorNodes2 != null)
            {
                foreach (var actorNode in actorNodes2)
                {
                    string actorName = actorNode.InnerText.Trim();
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//div[@class='director']");
            if (directorNode != null)
            {
                string directorName = directorNode.InnerText.Trim();
                result.People.Add(new PersonInfo { Name = directorName, Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken, this.cookies);
            if (detailsPageElements == null)
            {
                return images;
            }

            var imageNodes = detailsPageElements.SelectNodes("//div[@id='dv_frames']//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty).Replace("/320/", "/3840/").Replace("/10/", "/3840/").Replace("_320c.jpg", "_10.jpg");
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Backdrop });
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
