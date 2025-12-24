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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteLustomic : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//img[@alt='Video Preview']/following-sibling::p"))
                {
                    var titleNoFormatting = searchResult.InnerText.Trim();
                    var curID = Helper.Encode(searchURL);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//img[@alt='Video Preview']/following-sibling::p").InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//img[@alt='Video Description']/following-sibling::div").InnerText.Trim();
            movie.AddStudio("Lustomic");
            movie.AddCollection("Lustomic");

            var dateNode = doc.DocumentNode.SelectSingleNode("//*[@class='date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorsNode = doc.DocumentNode.SelectSingleNode("//p[contains(text(), 'Starring')]/span");
            if (actorsNode != null)
            {
                var actors = actorsNode.InnerText.Split(';').Select(a => a.Trim()).ToList();
                if (actors.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actors.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actors.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actorName in actors)
                {
                    result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imageNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, 'video_preview_images')]");
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        var imgUrl = img.GetAttributeValue("href", string.Empty);
                        images.Add(new RemoteImageInfo { Url = imgUrl });
                    }
                }
            }

            return images;
        }
    }
}
