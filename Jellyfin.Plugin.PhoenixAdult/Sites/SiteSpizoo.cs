using System;
using System.Collections.Generic;
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
    public class SiteSpizoo : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            // Simplified search logic, may need adjustments
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    var titleNode = doc.DocumentNode.SelectSingleNode(@"//h1");
                    var titleNoFormatting = titleNode?.InnerText.Trim();
                    var curID = Helper.Encode(sceneURL);
                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = titleNoFormatting,
                        SearchProviderName = Plugin.Instance.Name,
                    };
                    result.Add(item);
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

            movie.Name = doc.DocumentNode.SelectSingleNode(@"//h1")?.InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode(@"//p[@class=""description""] | //p[@class=""description-scene""] | //h2/following-sibling::p")?.InnerText.Trim();
            movie.AddStudio("Spizoo");

            var dateNode = doc.DocumentNode.SelectSingleNode(@"//p[@class=""date""]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = doc.DocumentNode.SelectNodes(@"//h3[text()='Pornstars:']/following-sibling::a");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    result.AddPerson(new PersonInfo
                    {
                        Name = actorNode.InnerText.Trim().Replace(".", ""),
                        Type = PersonKind.Actor,
                    });
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes(@"//div[@class='categories-holder']/a");
            if (genreNodes != null)
            {
                foreach (var genreNode in genreNodes)
                {
                    movie.AddGenre(genreNode.GetAttributeValue("title", string.Empty).Trim());
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            var posterNode = doc.DocumentNode.SelectSingleNode(@"//video[@id='the-video']");
            if (posterNode != null)
            {
                var posterUrl = posterNode.GetAttributeValue("poster", string.Empty);
                if (!string.IsNullOrEmpty(posterUrl))
                {
                    result.Add(new RemoteImageInfo
                    {
                        Url = posterUrl,
                        Type = ImageType.Primary,
                    });
                }
            }

            var backdropNodes = doc.DocumentNode.SelectNodes(@"//section[@id='photos-tour']//img");
            if (backdropNodes != null)
            {
                foreach (var backdropNode in backdropNodes)
                {
                    var backdropUrl = backdropNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(backdropUrl))
                    {
                        result.Add(new RemoteImageInfo
                        {
                            Url = backdropUrl,
                            Type = ImageType.Backdrop,
                        });
                    }
                }
            }

            return result;
        }
    }
}
