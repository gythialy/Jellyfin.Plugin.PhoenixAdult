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

namespace PhoenixAdult.Sites
{
    public class SiteJulesJordan : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle)) return result;

            string url = $"{Helper.GetSearchBaseURL(siteNum)}/trial/scenes/{searchTitle.ToLower().Replace(' ', '-')}_vids.html";
            var req = await HTTP.Request(url, cancellationToken);
            if (req.IsOK)
            {
                string curID = Helper.Encode(url);
                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } },
                    Name = $"{url} [{Helper.GetSearchSiteName(siteNum)}]",
                    SearchProviderName = Plugin.Instance.Name
                });
            }

            var searchPage = await HTML.ElementFromURL($"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}", cancellationToken);
            if (searchPage != null)
            {
                var searchResults = searchPage.SelectNodes("//div[@class='grid-item']");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        string titleNoFormatting = searchResult.SelectSingleNode("./a/img")?.GetAttributeValue("alt", "").Trim();
                        string sceneURL = searchResult.SelectSingleNode(".//a")?.GetAttributeValue("href", "");
                        string curID = Helper.Encode(sceneURL);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name
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
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='movie_title']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='player-scene-description']/span[contains(text(), 'Description:')]")?.ParentNode.InnerText.Replace("Description:", "").Trim();
            movie.AddStudio("Jules Jordan");

            var dvdName = detailsPageElements.SelectSingleNode("//div[@class='player-scene-description']//span[contains(text(), 'Movie:')]")?.ParentNode.InnerText.Replace("Movie:", "").Replace("Feature: ", "").Trim();
            if(!string.IsNullOrEmpty(dvdName))
                movie.AddTag(dvdName);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                var dateNode = detailsPageElements.SelectSingleNode("//div[@class='player-scene-description']//span[contains(text(), 'Date:')]")?.ParentNode.InnerText.Replace("Date:", "").Trim();
                if(DateTime.TryParse(dateNode, out parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//span[contains(text(), 'Categories')]/a");
            if (genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
            }

            var actorNodes = Helper.GetSearchSiteName(siteNum) == "GirlGirl"
                ? detailsPageElements.SelectNodes("//div[@class='item']/span/div/a")
                : detailsPageElements.SelectNodes("//div[@class='player-scene-description']/span[contains(text(), 'Starring:')]/..//a");

            if (actorNodes != null)
            {
                foreach(var actorLink in actorNodes)
                {
                    string actorName = actorLink.InnerText.Trim();
                    string actorPageURL = actorLink.GetAttributeValue("href", "");
                    var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                    string actorPhotoURL = actorPage?.SelectSingleNode("//img[@class='model_bio_thumb stdimage thumbs target']")?.GetAttributeValue("src0_3x", "");
                    if(!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                        actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                    result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonType.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var videoPoster = detailsPageElements.SelectSingleNode("//video[@id='video-player']")?.GetAttributeValue("poster", "");
            if(!string.IsNullOrEmpty(videoPoster))
                result.Add(new RemoteImageInfo { Url = videoPoster, Type = ImageType.Primary });

            var searchPageElements = await HTML.ElementFromURL($"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(item.Name)}", cancellationToken);
            if(searchPageElements != null)
            {
                for(int i = 0; i < 7; i++)
                {
                    var posterNode = searchPageElements.SelectSingleNode($"//img[contains(@id,'set-target')]/@src{i}_1x");
                    if (posterNode != null)
                    {
                        string posterUrl = posterNode.GetAttributeValue($"src{i}_1x", "");
                         if (!posterUrl.StartsWith("http"))
                            posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                        result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Backdrop });
                    }
                }
            }

            return result;
        }
    }
}
