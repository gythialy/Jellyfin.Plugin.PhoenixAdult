using System;
using System.Collections.Generic;
using System.Globalization;
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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteCaribbeancom : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string sceneId = searchTitle.Replace(" ", "-");
            string sceneUrl = Helper.GetSearchSearchURL(siteNum) + sceneId + "/index.html";
            var html = await HTML.ElementFromURL(sceneUrl, cancellationToken);

            if (html != null)
            {
                var titleNode = html.SelectSingleNode("//title");
                if (titleNode != null)
                {
                    string titleNoFormatting = titleNode.InnerText;
                    string curId = Helper.Encode(sceneUrl);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//title").InnerText;
            movie.AddStudio("caribbeancom");
            movie.AddTag("caribbeancom");

            var dateNode = detailsPageElements.SelectSingleNode("//span[@itemprop='uploadDate']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorSection = detailsPageElements.SelectSingleNode("//a[@itemprop='actor']//span[@itemprop='name']");
            if (actorSection != null)
            {
                foreach (var actorName in actorSection.InnerText.Split(','))
                {
                    result.People.Add(new PersonInfo { Name = actorName.Trim(), Type = PersonKind.Actor });
                }
            }

            var genreLinks = detailsPageElements.SelectNodes("//a[@itemprop='genre']");
            if (genreLinks != null)
            {
                foreach (var genreLink in genreLinks)
                {
                    movie.AddGenre(genreLink.InnerText.Trim());
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);

            string backgroundUrl = sceneUrl.Replace("/eng", string.Empty).Replace("index.html", "images/poster_en.jpg");
            images.Add(new RemoteImageInfo { Url = backgroundUrl, Type = ImageType.Primary });

            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken);
            if (detailsPageElements == null)
            {
                return images;
            }

            var posterNodes = detailsPageElements.SelectNodes("//img[@class='gallery-image']");
            if (posterNodes != null)
            {
                foreach (var poster in posterNodes)
                {
                    string posterUrl = poster.GetAttributeValue("src", string.Empty);
                    if (posterUrl.StartsWith("background-image"))
                    {
                        posterUrl = posterUrl.Split(new[] { "url(" }, StringSplitOptions.None)[1].Split(')')[0];
                    }

                    if (!posterUrl.StartsWith("http"))
                    {
                        posterUrl = Helper.GetSearchSearchURL(siteNum) + posterUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Backdrop });
                }
            }

            return images;
        }
    }
}
