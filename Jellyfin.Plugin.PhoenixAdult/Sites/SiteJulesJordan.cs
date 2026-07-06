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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteJulesJordan : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string url = $"{Helper.GetSearchBaseURL(siteNum)}/trial/scenes/{searchTitle.ToLower().Replace(' ', '-')}_vids.html";
            var req = await HTTP.Request(url, cancellationToken);
            if (req.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(req.Content);

                string title;
                string dateNode;
                if (siteNum[1] == 0)
                {
                    title = doc.DocumentNode.SelectSingleNode("//h1[@class='scene-title']")?.InnerText.Trim();
                    dateNode = doc.DocumentNode.SelectSingleNode("//div[div[contains(text(), 'Released')]]/div[@class='val']")?.InnerText.Trim();
                }
                else if (siteNum[1] == 1)
                {
                    title = doc.DocumentNode.SelectSingleNode("//span[@class='title_bar_hilite']")?.InnerText.Trim();
                    dateNode = doc.DocumentNode.SelectSingleNode("//div[@class='backgroundcolor_info']/div[@class='table']/div[@class='row']/div[contains(@class, 'update_date')]")?.InnerText.Trim();
                }
                else
                {
                    title = doc.DocumentNode.SelectSingleNode("//div[@class='movie_title']")?.InnerText.Trim();
                    dateNode = doc.DocumentNode.SelectSingleNode("//div[@class='player-scene-description']//span[contains(text(), 'Date:')]")?.ParentNode.InnerText.Replace("Date:", string.Empty).Trim();
                }

                var videoPoster = siteNum[1] == 1
                    ? doc.DocumentNode.SelectSingleNode("//img[contains(@id,'set-target')]")?.GetAttributeValue("src0_3x", string.Empty)
                    : doc.DocumentNode.SelectSingleNode("//video[@id='video-player']")?.GetAttributeValue("poster", string.Empty);

                string curID = Helper.Encode(url);
                string releaseDate = string.Empty;
                if (!string.IsNullOrEmpty(dateNode))
                {
                    if (siteNum[1] == 0)
                    {
                        string[] dateFormats = { "MM/dd/yyyy", "MMMM d, yyyy", "MMMM dd, yyyy" };
                        if (DateTime.TryParseExact(dateNode, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                            || DateTime.TryParse(dateNode, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }
                    }
                    else
                    {
                        if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }
                    }
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}" } },
                    Name = $"{title} ({dateNode}) [{Helper.GetSearchSiteName(siteNum)}]",
                    SearchProviderName = Plugin.Instance.Name,
                    ImageUrl = videoPoster,
                });
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
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;
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
            string dvdName = null;
            if (siteNum[1] == 0)
            {
                movie.Name = detailsPageElements.SelectSingleNode("//h1[@class='scene-title']")?.InnerText.Trim();
                movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='scene-desc']")?.InnerText.Trim();
                dvdName = null;
            }
            else if (siteNum[1] == 1)
            {
                movie.Name = detailsPageElements.SelectSingleNode("//span[@class='title_bar_hilite']")?.InnerText.Trim();
                movie.Overview = detailsPageElements.SelectSingleNode("//span[@class='update_description']")?.InnerText.Trim();
                dvdName = detailsPageElements.SelectSingleNode("//span[@class='update_dvds']")?.InnerText.Replace("Movie:", string.Empty).Replace("Feature: ", string.Empty).Trim();
            }
            else
            {
                movie.Name = detailsPageElements.SelectSingleNode("//div[@class='movie_title']")?.InnerText.Trim();
                movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='player-scene-description']/span[contains(text(), 'Description:')]")?.ParentNode.InnerText.Replace("Description:", string.Empty).Trim();
                dvdName = detailsPageElements.SelectSingleNode("//div[@class='player-scene-description']//span[contains(text(), 'Movie:')]")?.ParentNode.InnerText.Replace("Movie:", string.Empty).Replace("Feature: ", string.Empty).Trim();
            }

            if (!string.IsNullOrEmpty(dvdName))
            {
                movie.AddTag(dvdName);
            }

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                string dateNode;
                if (siteNum[1] == 0)
                {
                    dateNode = detailsPageElements.SelectSingleNode("//div[div[contains(text(), 'Released')]]/div[@class='val']")?.InnerText.Trim();
                }
                else if (siteNum[1] == 1)
                {
                    dateNode = detailsPageElements.SelectSingleNode("//div[@class='backgroundcolor_info']/div[@class='table']/div[@class='row']/div[contains(@class, 'update_date')]")?.InnerText.Trim();
                }
                else
                {
                    dateNode = detailsPageElements.SelectSingleNode("//div[@class='player-scene-description']//span[contains(text(), 'Date:')]")?.ParentNode.InnerText.Replace("Date:", string.Empty).Trim();
                }

                if (!string.IsNullOrEmpty(dateNode))
                {
                    if (siteNum[1] == 0)
                    {
                        string[] dateFormats = { "MM/dd/yyyy", "MMMM d, yyyy", "MMMM dd, yyyy" };
                        if (DateTime.TryParseExact(dateNode, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate)
                            || DateTime.TryParse(dateNode, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                            movie.PremiereDate = parsedDate;
                            movie.ProductionYear = parsedDate.Year;
                        }
                    }
                    else
                    {
                        if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                            movie.PremiereDate = parsedDate;
                            movie.ProductionYear = parsedDate.Year;
                        }
                    }
                }
            }

            HtmlNodeCollection genreNodes;
            if (siteNum[1] == 0)
            {
                genreNodes = detailsPageElements.SelectNodes("//div[@class='scene-cats']/a");
            }
            else if (siteNum[1] == 1)
            {
                genreNodes = detailsPageElements.SelectNodes("//span[contains(text(), 'Tags')]/a");
            }
            else
            {
                genreNodes = detailsPageElements.SelectNodes("//span[contains(text(), 'Categories')]/a");
            }

            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
                }
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            var actorNodes = Helper.GetSearchSiteName(siteNum) == "GirlGirl"
                ? detailsPageElements.SelectNodes("//div[@class='item']/span/div/a")
                : (
                    siteNum[1] == 0
                    ? detailsPageElements.SelectNodes("//div[@class='scene-info']//span[@class='update_models']/a")
                    : (
                        siteNum[1] == 1
                        ? detailsPageElements.SelectNodes("//div[@class='backgroundcolor_info']//span[@class='update_models']/a")
                        : detailsPageElements.SelectNodes("//div[@class='player-scene-description']/span[contains(text(), 'Starring:')]/..//a")
                    )
                );

            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    string actorName = actorLink.InnerText.Trim();
                    string actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                    var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                    var actorPhotoNode = actorPage?.SelectSingleNode("//img[@class='model_bio_thumb stdimage thumbs target']");
                    string actorPhotoURL = actorPhotoNode?.GetAttributeValue("src0_3x", string.Empty);
                    if (siteNum[1] == 0)
                    {
                        if (string.IsNullOrEmpty(actorPhotoURL))
                        {
                            actorPhotoURL = actorPhotoNode?.GetAttributeValue("src0_1x", string.Empty);
                        }

                        if (string.IsNullOrEmpty(actorPhotoURL))
                        {
                            actorPhotoURL = actorPhotoNode?.GetAttributeValue("src", string.Empty);
                        }
                    }

                    if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                    {
                        actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var videoPoster = siteNum[1] == 1
                ? detailsPageElements.SelectSingleNode("//img[contains(@id,'set-target')]")?.GetAttributeValue("src0_3x", string.Empty)
                : detailsPageElements.SelectSingleNode("//video[@id='video-player']")?.GetAttributeValue("poster", string.Empty);
            if (!string.IsNullOrEmpty(videoPoster))
            {
                result.Add(new RemoteImageInfo { Url = videoPoster, Type = ImageType.Primary });
            }

            if (siteNum[1] == 0)
            {
                var sceneImages = detailsPageElements.SelectNodes("//a[@class='tp-photo-thumb']/img");
                if (sceneImages != null)
                {
                    foreach (var imgNode in sceneImages)
                    {
                        string imgUrl = imgNode.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            if (!imgUrl.StartsWith("http"))
                            {
                                imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                            }

                            result.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Backdrop });
                        }
                    }
                }
            }

            var searchPageElements = await HTML.ElementFromURL($"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(item.Name)}", cancellationToken);
            if (searchPageElements != null)
            {
                for (int i = 0; i < 7; i++)
                {
                    var posterNode = searchPageElements.SelectSingleNode($"//img[contains(@id,'set-target')]/@src{i}_1x");
                    if (posterNode != null)
                    {
                        string posterUrl = posterNode.GetAttributeValue($"src{i}_1x", string.Empty);
                        if (!posterUrl.StartsWith("http"))
                        {
                            posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                        }

                        result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Backdrop });
                    }
                }
            }

            return result;
        }
    }
}
