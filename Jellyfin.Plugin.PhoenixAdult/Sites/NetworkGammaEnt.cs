using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkGammaEnt : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            var searchData = new SearchData(searchTitle, searchDate);

            bool networkscene = true;
            bool networkscenepages = true;
            bool networkdvd = true;
            string network = string.Empty;
            string network_sep_scene_prev = string.Empty;
            string network_sep_scene = string.Empty;
            string network_sep_scene_pages_prev = string.Empty;
            string network_sep_scene_pages = "/";
            string network_sep_scene_pages_next = string.Empty;
            string network_sep_dvd_prev = string.Empty;
            string network_sep_dvd = "/1/dvd";

            int sNum = siteNum[1];

            if (sNum == 0)
            {
                network = "Fame Digital";
                networkscene = false;
                networkscenepages = false;
                networkdvd = false;
            }
            else if (sNum >= 1 && sNum <= 6)
            {
                network = "Open Life Network";
                networkdvd = false;
            }

            if (network.Equals(Helper.GetSearchSiteName(siteNum), StringComparison.OrdinalIgnoreCase))
            {
                network = string.Empty;
            }

            if (networkscene)
            {
                string searchUrl = Helper.GetSearchSearchURL(siteNum) + network_sep_scene_prev + searchData.Encoded + network_sep_scene;
                var searchResultsNode = await HTML.ElementFromURL(searchUrl, cancellationToken);
                if (searchResultsNode != null)
                {
                    var searchResults = searchResultsNode.SelectNodes("//div[@class='tlcDetails']");
                    if (searchResults != null)
                    {
                        foreach (var searchResult in searchResults)
                        {
                            var titleNode = searchResult.SelectSingleNode(".//a[1]");
                            string titleNoFormatting = Helper.ParseTitle(titleNode.InnerText.Trim().Replace("BONUS-", "BONUS - ").Replace("BTS-", "BTS - "), siteNum);
                            string curID = Helper.Encode(titleNode.GetAttributeValue("href", string.Empty));
                            string releaseDateStr = ParseReleaseDate(searchResult, cancellationToken).Result;

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{titleNoFormatting} [{network}/{Helper.GetSearchSiteName(siteNum)}] {releaseDateStr}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
                }
            }

            // DVD Search
            if (networkdvd)
            {
                string searchUrl = Helper.GetSearchSearchURL(siteNum) + network_sep_dvd_prev + searchData.Encoded + network_sep_dvd;
                var dvdResultsNode = await HTML.ElementFromURL(searchUrl, cancellationToken);
                if (dvdResultsNode != null)
                {
                    var dvdResults = dvdResultsNode.SelectNodes("//div[contains(@class, 'tlcItem playlistable_dvds')] | //div[@class='tlcDetails']");
                    if (dvdResults != null)
                    {
                        foreach (var dvdResult in dvdResults)
                        {
                            var titleNode = dvdResult.SelectSingleNode(".//div[@class='tlcTitle']/a");
                            string titleNoFormatting = titleNode.GetAttributeValue("title", string.Empty).Trim();
                            string curID = Helper.Encode(titleNode.GetAttributeValue("href", string.Empty));
                            string releaseDateStr = ParseReleaseDate(dvdResult, cancellationToken).Result;

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{titleNoFormatting} ({(string.IsNullOrEmpty(releaseDateStr) ? string.Empty : DateTime.Parse(releaseDateStr).Year.ToString())}) - Full Movie [{Helper.GetSearchSiteName(siteNum)}]",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
                }
            }

            return result;
        }

        private async Task<string> ParseReleaseDate(HtmlNode node, CancellationToken cancellationToken)
        {
            var dateNode = node.SelectSingleNode(".//div[@class='tlcSpecs']/span[@class='tlcSpecsDate']/span[@class='tlcDetailsValue']");
            if (dateNode != null)
            {
                return DateTime.Parse(dateNode.InnerText.Trim()).ToString("yyyy-MM-dd");
            }

            var sceneUrlNode = node.SelectSingleNode(".//a[1]");
            if (sceneUrlNode != null)
            {
                var scenePage = await HTML.ElementFromURL(Helper.GetSearchBaseURL(new[] { 0 }) + sceneUrlNode.GetAttributeValue("href", string.Empty), cancellationToken);
                if (scenePage != null)
                {
                    var updatedDateNode = scenePage.SelectSingleNode("//*[@class='updatedDate']");
                    if (updatedDateNode != null)
                    {
                        return DateTime.Parse(updatedDateNode.InnerText.Trim()).ToString("yyyy-MM-dd");
                    }
                }
            }

            return string.Empty;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;

            // Title
            movie.Name = Helper.ParseTitle(
                detailsPageElements.SelectSingleNode("//h1[@class='sceneTitle']")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", string.Empty).Trim()
                ?? detailsPageElements.SelectSingleNode("//h3[@class='dvdTitle']")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim(), siteNum);

            // Summary
            movie.Overview = detailsPageElements.SelectSingleNode("//meta[@name='twitter:description']")?.GetAttributeValue("content", string.Empty).Trim()
               ?? detailsPageElements.SelectSingleNode("//div[@class='sceneDesc bioToRight showMore']")?.InnerText.Trim().Substring(20)
               ?? detailsPageElements.SelectSingleNode("//div[@class='sceneDescText']")?.InnerText.Trim()
               ?? detailsPageElements.SelectSingleNode("//p[@class='descriptionText']")?.InnerText.Trim();

            // Studio and Collections
            string studio = "Gamma Entertainment";
            movie.AddStudio(studio);
            string tagline = detailsPageElements.SelectSingleNode("//div[@class='studioLink']")?.InnerText.Trim() ?? Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var dvdTitleNode = detailsPageElements.SelectSingleNode("//a[contains(@class, 'dvdLink')][1]");
            if (dvdTitleNode != null)
            {
                movie.AddTag(dvdTitleNode.GetAttributeValue("title", string.Empty).Replace("#0", string.Empty).Replace("#", string.Empty));
            }

            // Genres
            var genreNodes = detailsPageElements.SelectNodes("//div[@class='sceneCol sceneColCategories']//a | //div[@class='sceneCategories']//a | //p[@class='dvdCol']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
                }
            }

            // Release Date
            var dateNode = detailsPageElements.SelectSingleNode("//*[@class='updatedDate']")?.InnerText.Replace("|", string.Empty).Trim()
                ?? detailsPageElements.SelectSingleNode("//*[@class='updatedOn']")?.InnerText.Trim().Substring(8).Trim();
            if (!string.IsNullOrEmpty(dateNode) && DateTime.TryParse(dateNode, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            // Actors
            var actorNodes = detailsPageElements.SelectNodes("//div[@class='sceneCol sceneColActors']//a | //div[@class='sceneCol sceneActors']//a | //div[@class='pornstarNameBox']/a[@class='pornstarName'] | //div[@id='slick_DVDInfoActorCarousel']//a | //div[@id='slick_sceneInfoPlayerActorCarousel']//a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    string actorName = actorLink.InnerText.Trim();
                    string actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                    var actorPage = await HTML.ElementFromURL(Helper.GetSearchBaseURL(siteNum) + actorPageURL, cancellationToken);
                    string actorPhotoURL = actorPage?.SelectSingleNode("//img[@class='actorPicture'] | //span[@class='removeAvatarParent']/img")?.GetAttributeValue("src", string.Empty);
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            // Director
            var directorNodes = detailsPageElements.SelectNodes("//div[@class='sceneCol sceneColDirectors']//a | //ul[@class='directedBy']/li/a");
            if (directorNodes != null)
            {
                foreach (var director in directorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = director.InnerText.Trim(), Type = PersonKind.Director });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return images;
            }

            var imageUrls = new List<string>();

            var twitterBG = detailsPageElements.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", string.Empty);
            if (!string.IsNullOrEmpty(twitterBG))
            {
                imageUrls.Add(twitterBG);
            }

            var sceneImg = detailsPageElements.SelectSingleNode("//img[@class='sceneImage']")?.GetAttributeValue("src", string.Empty);
            if (!string.IsNullOrEmpty(sceneImg))
            {
                imageUrls.Add(sceneImg);
            }

            if (sceneURL.Contains("/movie/"))
            {
                var dvdFrontCover = detailsPageElements.SelectSingleNode("//a[@class='frontCoverImg']")?.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(dvdFrontCover))
                {
                    imageUrls.Add(dvdFrontCover);
                }

                var dvdBackCover = detailsPageElements.SelectSingleNode("//a[@class='backCoverImg']")?.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(dvdBackCover))
                {
                    imageUrls.Add(dvdBackCover);
                }

                var sceneImgNodes = detailsPageElements.SelectNodes("//img[@class='tlcImageItem img'] | //img[@class='img lazy']");
                if (sceneImgNodes != null)
                {
                    foreach (var img in sceneImgNodes)
                    {
                        imageUrls.Add(img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("data-original", string.Empty));
                    }
                }
            }

            bool first = true;
            foreach (var imageUrl in imageUrls.Distinct())
            {
                if (string.IsNullOrEmpty(imageUrl))
                {
                    continue;
                }

                var imageInfo = new RemoteImageInfo { Url = imageUrl };
                if (first)
                {
                    imageInfo.Type = ImageType.Primary;
                    first = false;
                }
                else
                {
                    imageInfo.Type = ImageType.Backdrop;
                }

                images.Add(imageInfo);
            }

            return images;
        }
    }

    class SearchData
    {
        public string SearchTitle { get; }

        public DateTime? SearchDate { get; }

        public string Encoded { get; }

        public SearchData(string title, DateTime? date)
        {
            SearchTitle = title;
            SearchDate = date;
            Encoded = Uri.EscapeDataString(title).Replace("%27", string.Empty).Replace("%3F", string.Empty).Replace("%2C", string.Empty);
        }
    }
}
