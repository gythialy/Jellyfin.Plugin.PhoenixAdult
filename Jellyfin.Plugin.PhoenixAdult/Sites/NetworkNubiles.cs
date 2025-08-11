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
    public class NetworkNubiles : IProviderBase
    {
        private readonly IDictionary<string, string> _cookies = new Dictionary<string, string> { { "18-plus-modal", "hidden" } };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle)) return result;

            if (searchDate.HasValue)
            {
                var url = $"{Helper.GetSearchSearchURL(siteNum)}date/{searchDate.Value:yyyy-MM-dd}/{searchDate.Value:yyyy-MM-dd}";
                var data = await HTML.ElementFromURL(url, cancellationToken, null, _cookies);
                if (data == null) return result;

                var searchResults = data.SelectNodes("//div[contains(@class, 'content-grid-item')]");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        var titleParts = searchResult.SelectSingleNode(".//span[@class='title']/a")?.InnerText.Split('-');
                        string titleNoFormatting = titleParts.Length > 1 ? $"{titleParts[0].Trim()} - {titleParts[1].Trim()}" : titleParts[0].Trim();
                        string curID = searchResult.SelectSingleNode(".//span[@class='title']/a")?.GetAttributeValue("href", "").Split('/')[3];
                        string releaseDate = DateTime.Parse(searchResult.SelectSingleNode(".//span[@class='date']")?.InnerText.Trim()).ToString("yyyy-MM-dd");

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name
                        });
                    }
                }
            }
            else if (int.TryParse(searchTitle.Split(' ')[0], out var sceneNum))
            {
                var url = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneNum}";
                var detailsPageElements = await HTML.ElementFromURL(url, cancellationToken, null, _cookies);
                if (detailsPageElements != null)
                {
                    var titleNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-pane-title')]//h2");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string releaseDate = DateTime.Parse(detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-pane')]//span[@class='date']")?.InnerText.Trim()).ToString("yyyy-MM-dd");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{sceneNum}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
                    });
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

            string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneID[0].Split('|')[0]}";
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, null, _cookies);
            if (sceneData == null) return result;

            var movie = (Movie)result.Item;
            var titleParts = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-title')]//h2")?.InnerText.Split('-');
            movie.Name = titleParts.Length > 1 ? $"{titleParts[0].Trim()} - {titleParts[1].Trim()}" : titleParts[0].Trim();

            var descriptionNode = sceneData.SelectSingleNode("//div[@class='col-12 content-pane-column']/div");
            string description = descriptionNode?.InnerText;
            if (string.IsNullOrEmpty(description))
            {
                var paragraphs = sceneData.SelectNodes("//div[@class='col-12 content-pane-column']//p");
                if(paragraphs != null)
                    description = string.Join("\n\n", paragraphs.Select(p => p.InnerText.Trim()));
            }
            movie.Overview = description?.Trim();

            movie.AddStudio("Nubiles");
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            var sceneDate = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane')]//span[@class='date']")?.InnerText.Trim();
            if (DateTime.TryParse(sceneDate, out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            var genreNode = sceneData.SelectNodes("//div[@class='categories']/a");
            if(genreNode != null)
            {
                foreach (var genreLink in genreNode)
                    movie.AddGenre(genreLink.InnerText.Trim());
            }

            var actorsNode = sceneData.SelectNodes("//div[contains(@class, 'content-pane-performer')]/a");
            if (actorsNode != null)
            {
                foreach (var actorLink in actorsNode)
                {
                    string actorName = actorLink.InnerText.Trim();
                    string actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorLink.GetAttributeValue("href", "");
                    var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken, null, _cookies);
                    string actorPhotoURL = "http:" + actorPage?.SelectSingleNode("//div[contains(@class, 'model-profile')]//img")?.GetAttributeValue("src", "");
                    result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonType.Actor });
                }
            }

            // Add male actors from summary
            if(!string.IsNullOrEmpty(movie.Overview))
            {
                var maleActors = new[] { "Logan Long", "Patrick Delphia", "Seth Gamble", "Alex D.", "Lucas Frost", "Van Wylde", "Tyler Nixon", "Logan Pierce", "Johnny Castle", "Damon Dice", "Scott Carousel", "Dylan Snow", "Michael Vegas", "Xander Corvus", "Chad White" };
                foreach(var actor in maleActors)
                {
                    if (movie.Overview.Contains(actor, StringComparison.OrdinalIgnoreCase))
                        result.People.Add(new PersonInfo { Name = actor, Type = PersonType.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneID[0].Split('|')[0]}";
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, null, _cookies);
            if (sceneData == null) return result;

            var poster = sceneData.SelectSingleNode("//video/@poster")?.GetAttributeValue("poster", "");
            if (!string.IsNullOrEmpty(poster) && !poster.StartsWith("http"))
                poster = "http:" + poster;
            if(!string.IsNullOrEmpty(poster))
                result.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Primary });

            string galleryURL = string.Empty;
            var photoLink = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-related-links')]/a[contains(., 'Pic')]");
            if (photoLink != null)
            {
                galleryURL = Helper.GetSearchBaseURL(siteNum) + photoLink.GetAttributeValue("href", "");
            }
            else if (!string.IsNullOrEmpty(poster))
            {
                var match = new Regex(@"(?<=videos\/).*(?=\/sample)").Match(poster);
                if (match.Success)
                    galleryURL = $"{Helper.GetSearchBaseURL(siteNum)}/galleries/{match.Value}/screenshots";
            }

            if (!string.IsNullOrEmpty(galleryURL))
            {
                var photoPage = await HTML.ElementFromURL(galleryURL, cancellationToken, null, _cookies);
                if (photoPage != null)
                {
                    var sceneImages = photoPage.SelectNodes("//div[@class='img-wrapper']//picture/source[1]");
                    if (sceneImages != null)
                    {
                        foreach (var sceneImage in sceneImages)
                        {
                            string posterURL = sceneImage.GetAttributeValue("srcset", "");
                            if (!string.IsNullOrEmpty(posterURL))
                                result.Add(new RemoteImageInfo { Url = "http:" + posterURL, Type = ImageType.Backdrop });
                        }
                    }
                }
            }

            return result;
        }
    }
}
