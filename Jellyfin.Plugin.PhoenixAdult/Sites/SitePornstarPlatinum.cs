using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SitePornstarPlatinum : IProviderBase
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
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='item no-nth ']"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode("./div[@class='item-content']/h3/a").InnerText;
                    var sceneURL = searchResult.SelectSingleNode("./div[@class='item-content']/h3/a").GetAttributeValue("href", string.Empty);
                    var posterURL = searchResult.SelectSingleNode("./div[@class='item-header']/a/img").GetAttributeValue("rel", string.Empty);
                    var actorName = searchResult.SelectSingleNode("./div[@class='item-content']/div[@style='overflow:hidden']/span[@class='marker left']")?.InnerText.Trim() ?? string.Empty;
                    var releaseDate = string.Empty;
                    var dateNode = searchResult.SelectSingleNode("./div[@class='item-content']/div[@style='overflow:hidden']/span[@class='left content-date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var curID = Helper.Encode($"{sceneURL}|{titleNoFormatting}|{releaseDate}|{posterURL}|{actorName}");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} {releaseDate} [{Helper.GetSearchSiteName(siteNum)}]",
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
            var sceneData = Helper.Decode(sceneID[0]).Split('|');
            var sceneURL = sceneData[0];
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = sceneData[1].Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[@class='panel-content']/p").InnerText.Trim();
            movie.AddStudio("Pornstar Platinum");
            movie.AddCollection("Pornstar Platinum");

            if (DateTime.TryParse(sceneData[2], out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//div[@class='tagcloud']/a"))
            {
                movie.AddGenre(genreLink.InnerText.Trim());
            }

            result.AddPerson(new PersonInfo { Name = sceneData[4], Type = PersonKind.Actor });

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var posterURL = Helper.Decode(sceneID[0]).Split('|')[3];
            if (!string.IsNullOrEmpty(posterURL))
            {
                images.Add(new RemoteImageInfo { Url = posterURL, Type = ImageType.Primary });
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
