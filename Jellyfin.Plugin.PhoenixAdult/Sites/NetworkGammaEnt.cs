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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkGammaEnt : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null) return result;

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

            int sNum = siteNum[0];

            if (sNum == 278 || (sNum >= 285 && sNum <= 287) || sNum == 843)
            {
                network = "XEmpire";
                network_sep_scene_prev = "scene/";
                network_sep_scene_pages_prev = "scene/";
                network_sep_dvd_prev = "dvd/";
                network_sep_dvd = "/1";
            }
            else if (sNum == 329 || (sNum >= 351 && sNum <= 354) || sNum == 861)
            {
                network = "Blowpass";
                networkdvd = false;
            }
            else if (sNum == 330 || (sNum >= 355 && sNum <= 360) || sNum == 750)
            {
                network = "Fantasy Massage";
                networkdvd = false;
                network_sep_scene = "/scene";
                network_sep_scene_pages = "/scene/";
            }
            else if ((sNum >= 365 && sNum <= 372) || sNum == 466 || sNum == 692)
            {
                network = "21Sextury";
                networkdvd = false;
            }
            else if (sNum == 183 || (sNum >= 373 && sNum <= 374))
            {
                network = "21Naturals";
                networkdvd = false;
                network_sep_scene = "/scene";
                network_sep_scene_pages = "/scene/";
            }
            else if (sNum >= 383 && sNum <= 386)
            {
                network = "Fame Digital";
                if (sNum == 383)
                {
                    networkdvd = false;
                    network_sep_scene = "/scene";
                    network_sep_scene_pages = "/scene/";
                    network_sep_dvd = "/dvd";
                }
                if (sNum == 386)
                {
                    networkscene = false;
                    networkscenepages = false;
                    networkdvd = false;
                }
            }
            else if (sNum >= 387 && sNum <= 392)
            {
                network = "Open Life Network";
                networkdvd = false;
            }
            else if (sNum == 281)
            {
                network = "Pure Taboo";
                networkdvd = false;
                network_sep_scene = "/scene";
                network_sep_scene_pages = "/scene/";
            }
            else if (sNum == 381)
            {
                network = "Burning Angel";
                networkdvd = false;
                network_sep_scene = "/scene";
                network_sep_scene_pages = "/scene/";
            }
            else if (sNum == 382)
            {
                network = "Pretty Dirty";
                networkdvd = false;
                network_sep_scene = "/scene";
                network_sep_scene_pages = "/scene/";
            }
            else if (sNum >= 460 && sNum <= 465)
            {
                network = "21Sextreme";
                networkdvd = false;
                network_sep_scene = "/scene";
                network_sep_scene_pages = "/scene/";
            }

            if (network.Equals(Helper.GetSearchSiteName(siteNum), StringComparison.OrdinalIgnoreCase))
                network = string.Empty;

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
                            string titleNoFormatting = titleNode.InnerText.Trim().Replace("BONUS-", "BONUS - ").Replace("BTS-", "BTS - ");
                            string curID = Helper.Encode(titleNode.GetAttributeValue("href", ""));
                            string releaseDateStr = ParseReleaseDate(searchResult, cancellationToken).Result;

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{sNum}" } },
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
                            string titleNoFormatting = titleNode.GetAttributeValue("title", "").Trim();
                            string curID = Helper.Encode(titleNode.GetAttributeValue("href", ""));
                            string releaseDateStr = ParseReleaseDate(dvdResult, cancellationToken).Result;

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{sNum}" } },
                                Name = $"{titleNoFormatting} ({(string.IsNullOrEmpty(releaseDateStr) ? "" : DateTime.Parse(releaseDateStr).Year.ToString())}) - Full Movie [{Helper.GetSearchSiteName(siteNum)}]",
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
                return DateTime.Parse(dateNode.InnerText.Trim()).ToString("yyyy-MM-dd");

            var sceneUrlNode = node.SelectSingleNode(".//a[1]");
            if (sceneUrlNode != null)
            {
                var scenePage = await HTML.ElementFromURL(Helper.GetSearchBaseURL(new[] { 0 }) + sceneUrlNode.GetAttributeValue("href", ""), cancellationToken);
                if (scenePage != null)
                {
                    var updatedDateNode = scenePage.SelectSingleNode("//*[@class='updatedDate']");
                    if (updatedDateNode != null)
                        return DateTime.Parse(updatedDateNode.InnerText.Trim()).ToString("yyyy-MM-dd");
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

            int sNum = int.Parse(sceneID[0].Split('|')[1]);
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(new [] {sNum}) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;

            // Title
            movie.Name = detailsPageElements.SelectSingleNode("//h1[@class='sceneTitle']")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", "").Trim()
                ?? detailsPageElements.SelectSingleNode("//h3[@class='dvdTitle']")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();

            // Summary
             movie.Overview = detailsPageElements.SelectSingleNode("//meta[@name='twitter:description']")?.GetAttributeValue("content", "").Trim()
                ?? detailsPageElements.SelectSingleNode("//div[@class='sceneDesc bioToRight showMore']")?.InnerText.Trim().Substring(20)
                ?? detailsPageElements.SelectSingleNode("//div[@class='sceneDescText']")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//p[@class='descriptionText']")?.InnerText.Trim();

            // Studio and Collections
            string studio = GetStudio(sNum);
            movie.AddStudio(studio);
            string tagline = detailsPageElements.SelectSingleNode("//div[@class='studioLink']")?.InnerText.Trim() ?? Helper.GetSearchSiteName(new [] {sNum});
            movie.AddTag(tagline);

            var dvdTitleNode = detailsPageElements.SelectSingleNode("//a[contains(@class, 'dvdLink')][1]");
            if (dvdTitleNode != null)
                movie.AddTag(dvdTitleNode.GetAttributeValue("title", "").Replace("#0", "").Replace("#", ""));

            // Genres
            var genreNodes = detailsPageElements.SelectNodes("//div[@class='sceneCol sceneColCategories']//a | //div[@class='sceneCategories']//a | //p[@class='dvdCol']/a");
            if (genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
            }

            // Release Date
            var dateNode = detailsPageElements.SelectSingleNode("//*[@class='updatedDate']")?.InnerText.Replace("|", "").Trim()
                ?? detailsPageElements.SelectSingleNode("//*[@class='updatedOn']")?.InnerText.Trim().Substring(8).Trim();
            if(!string.IsNullOrEmpty(dateNode) && DateTime.TryParse(dateNode, out var parsedDate))
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
                    string actorPageURL = actorLink.GetAttributeValue("href", "");
                    var actorPage = await HTML.ElementFromURL(Helper.GetSearchBaseURL(new [] {sNum}) + actorPageURL, cancellationToken);
                    string actorPhotoURL = actorPage?.SelectSingleNode("//img[@class='actorPicture'] | //span[@class='removeAvatarParent']/img")?.GetAttributeValue("src", "");
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            // Director
            var directorNodes = detailsPageElements.SelectNodes("//div[@class='sceneCol sceneColDirectors']//a | //ul[@class='directedBy']/li/a");
            if(directorNodes != null)
            {
                foreach(var director in directorNodes)
                    result.People.Add(new PersonInfo { Name = director.InnerText.Trim(), Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            int sNum = int.Parse(sceneID[0].Split('|')[1]);
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(new [] {sNum}) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return images;

            var imageUrls = new List<string>();

            var twitterBG = detailsPageElements.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", "");
            if (!string.IsNullOrEmpty(twitterBG)) imageUrls.Add(twitterBG);

            var sceneImg = detailsPageElements.SelectSingleNode("//img[@class='sceneImage']")?.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(sceneImg)) imageUrls.Add(sceneImg);

            if (sceneURL.Contains("/movie/"))
            {
                var dvdFrontCover = detailsPageElements.SelectSingleNode("//a[@class='frontCoverImg']")?.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(dvdFrontCover)) imageUrls.Add(dvdFrontCover);

                var dvdBackCover = detailsPageElements.SelectSingleNode("//a[@class='backCoverImg']")?.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(dvdBackCover)) imageUrls.Add(dvdBackCover);

                var sceneImgNodes = detailsPageElements.SelectNodes("//img[@class='tlcImageItem img'] | //img[@class='img lazy']");
                if(sceneImgNodes != null)
                {
                    foreach(var img in sceneImgNodes)
                        imageUrls.Add(img.GetAttributeValue("src", "") ?? img.GetAttributeValue("data-original", ""));
                }
            }

            bool first = true;
            foreach (var imageUrl in imageUrls.Distinct())
            {
                if (string.IsNullOrEmpty(imageUrl)) continue;
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

        private string GetStudio(int siteNum)
        {
            if (siteNum == 278 || (siteNum >= 285 && siteNum <= 287) || siteNum == 843) return "XEmpire";
            if (siteNum == 329 || (siteNum >= 351 && siteNum <= 354) || siteNum == 861) return "Blowpass";
            if (siteNum == 331 || (siteNum >= 355 && siteNum <= 360) || siteNum == 750) return "Fantasy Massage";
            if ((siteNum >= 365 && siteNum <= 372) || siteNum == 466 || siteNum == 690) return "21Sextury";
            if (siteNum == 183 || (siteNum >= 373 && siteNum <= 374)) return "21Naturals";
            if (siteNum >= 383 && siteNum <= 386) return "Fame Digital";
            if (siteNum >= 387 && siteNum <= 392) return "Open Life Network";
            if (siteNum == 281) return "Pure Taboo";
            if (siteNum == 381) return "Burning Angel";
            if (siteNum == 382) return "Pretty Dirty";
            if (siteNum >= 460 && siteNum <= 466) return "21Sextreme";
            return "Gamma Entertainment";
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
            Encoded = Uri.EscapeDataString(title).Replace("%27", "").Replace("%3F", "").Replace("%2C", "");
        }
    }
}
