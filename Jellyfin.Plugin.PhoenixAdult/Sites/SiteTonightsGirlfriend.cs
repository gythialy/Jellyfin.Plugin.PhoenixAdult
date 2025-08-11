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
using Jellyfin.Data.Enums;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteTonightsGirlfriend : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle)) return result;

            string searchString = searchTitle.ToLower().Split("and ")[0].Trim().Replace(' ', '-');

            for (int i = 1; i < 5; i++)
            {
                var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchString}/?p={i}";
                var searchPageElements = await HTML.ElementFromURL(searchUrl, cancellationToken);
                if (searchPageElements == null) break;

                var searchResults = searchPageElements.SelectNodes("//div[@class='panel-body']");
                if (searchResults == null) break;

                foreach (var searchResult in searchResults)
                {
                    var actorNodes = searchResult.SelectNodes(".//span[@class='scene-actors']//a");
                    var actorList = actorNodes?.Select(a => a.InnerText).ToList() ?? new List<string>();
                    string titleNoFormatting = string.Join(", ", actorList);
                    string firstActor = actorList.FirstOrDefault() ?? "";
                    string sceneURL = searchResult.SelectSingleNode(".//a")?.GetAttributeValue("href", "").Split('?')[0];
                    string curID = Helper.Encode(sceneURL);
                    string releaseDate = DateTime.Parse(searchResult.SelectSingleNode(".//span[@class='scene-date']")?.InnerText.Trim()).ToString("yyyy-MM-dd");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [Tonight's Girlfriend] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
                if (searchResults.Count < 9) break;
            }
            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>() { Item = new Movie(), People = new List<PersonInfo>() };

            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;

            var actorList = new List<string>();
            var actors = detailsPageElements.SelectNodes("//p[@class='grey-performers']//a");
            string sceneInfo = detailsPageElements.SelectSingleNode("//p[@class='grey-performers']")?.InnerText;

            foreach(var actorLink in actors)
            {
                string actorName = actorLink.InnerText.Trim();
                actorList.Add(actorName);
                sceneInfo = sceneInfo.Replace(actorName + ",", "").Trim();

                string actorPageURL = actorLink.GetAttributeValue("href", "").Split('?')[0];
                var actorPageElements = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                string actorPhotoURL = "https:" + actorPageElements?.SelectSingleNode("//div[contains(@class, 'performer-details')]//img")?.GetAttributeValue("src", "");
                result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
            }

            movie.Name = string.Join(", ", actorList);
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='scene-description']")?.InnerText.Trim();
            movie.AddStudio("Naughty America");
            movie.AddTag("Tonight's Girlfriend");

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var maleActors = sceneInfo.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
            foreach(var maleActor in maleActors)
                result.People.Add(new PersonInfo { Name = maleActor.Trim(), Type = PersonKind.Actor });

            var genres = new List<string> { "Girlfriend Experience", "Pornstar", "Hotel", "Pornstar Experience" };
            if (result.People.Count == 3)
            {
                genres.Add("Threesome");
                genres.Add(actors.Count == 2 ? "BGG" : "BBG");
            }
            foreach(var genre in genres)
                movie.AddGenre(genre);

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

            string posterUrl = "https:" + detailsPageElements.SelectSingleNode("//img[@class='playcard']")?.GetAttributeValue("src", "");
            result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });

            string backdropUrl = posterUrl.Split(new[] { "scene/image", "scene/horizontal" }, StringSplitOptions.None)[0] + "scene/vertical/390x590cdynamic.jpg";
            result.Add(new RemoteImageInfo { Url = backdropUrl, Type = ImageType.Backdrop });

            return result;
        }
    }
}
