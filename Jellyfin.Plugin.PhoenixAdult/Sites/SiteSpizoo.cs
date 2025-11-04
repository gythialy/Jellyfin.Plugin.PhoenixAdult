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
            var result = new List<RemoteSearchResult>();
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(" ", "+")}";
            var http = await HTTP.Request(searchUrl, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            var searchResults = doc.DocumentNode.SelectNodes(@"//div[@class='model-update row']");
            if (searchResults == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults)
            {
                var titleNode = searchResult.SelectSingleNode(@".//h3[@class='titular']");
                var title = titleNode?.InnerText.Trim();

                var urlNode = searchResult.SelectSingleNode(@".//a");
                var sceneUrl = urlNode?.GetAttributeValue("href", string.Empty);

                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(sceneUrl))
                {
                    continue;
                }

                var dateNode = searchResult.SelectSingleNode(@".//div[@class='date-label']");
                var date = string.Empty;
                if (dateNode != null)
                {
                    var dateText = dateNode.InnerText.Replace("Released date:", string.Empty).Trim();
                    if (DateTime.TryParse(dateText, out var parsedDate))
                    {
                        date = parsedDate.ToString("yyyy-MM-dd");
                    }
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneUrl) } },
                    Name = $"{title} [{date}]",
                    SearchProviderName = Plugin.Instance.Name,
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
                        Name = actorNode.InnerText.Trim().Replace(".", string.Empty),
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
