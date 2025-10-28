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
    public class NetworkPornCZ : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            string encodedSearchTitle = searchTitle.Replace(" ", "+").Replace("--", "+").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + encodedSearchTitle;
            var data = await HTML.ElementFromURL(url, cancellationToken);
            if (data == null)
            {
                return result;
            }

            var searchResults = data.SelectNodes("//div[@data-href]");
            if (searchResults == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults)
            {
                var titleNode = searchResult.SelectSingleNode(".//h4");
                string titleNoFormatting = titleNode?.InnerText.Trim();
                string sceneURL = Helper.GetSearchBaseURL(siteNum) + searchResult.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty);
                string curID = Helper.Encode(sceneURL);
                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}" } },
                    Name = $"{titleNoFormatting} [PornCZ/{Helper.GetSearchSiteName(siteNum)}]",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (sceneData == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = sceneData.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = sceneData.SelectSingleNode("//div[@class='heading-detail']/p[2]")?.InnerText.Trim();
            movie.AddStudio("PornCZ");
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            var dateNode = sceneData.SelectSingleNode("//meta[@property='video:release_date']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.GetAttributeValue("content", string.Empty).Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedSceneDate))
            {
                movie.PremiereDate = parsedSceneDate;
                movie.ProductionYear = parsedSceneDate.Year;
            }

            var genreNodes = sceneData.SelectNodes("//div[contains(., 'Genres')]/a");
            if (genreNodes != null)
            {
                foreach (var genreLink in genreNodes)
                {
                    movie.AddGenre(genreLink.InnerText.Trim());
                }
            }

            var actorNodes = sceneData.SelectNodes("//div[contains(., 'Actors')]/a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    string actorName = actorLink.InnerText.Trim();
                    string actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                    if (!actorPageURL.StartsWith("http"))
                    {
                        actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorPageURL;
                    }

                    var actorHTML = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                    var imgNode = actorHTML?.SelectSingleNode("//div[@class='model-heading-photo']//@src");
                    string actorPhoto = imgNode?.GetAttributeValue("src", string.Empty);

                    if (actorPhoto?.Contains("blank") == false)
                    {
                        if (!actorPhoto.StartsWith("http"))
                        {
                            actorPhoto = Helper.GetSearchBaseURL(siteNum) + actorPhoto;
                        }

                        result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhoto, Type = PersonKind.Actor });
                    }
                    else
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (sceneData == null)
            {
                return result;
            }

            var imageNodes = sceneData.SelectNodes("//div[@id='photos']//@data-src");
            if (imageNodes != null)
            {
                bool first = true;
                foreach (var imageNode in imageNodes)
                {
                    string img = imageNode.GetAttributeValue("data-src", string.Empty);
                    if (!img.StartsWith("http"))
                    {
                        img = Helper.GetSearchBaseURL(siteNum) + img;
                    }

                    var imageInfo = new RemoteImageInfo { Url = img };
                    if (first)
                    {
                        imageInfo.Type = ImageType.Primary;
                        first = false;
                    }
                    else
                    {
                        imageInfo.Type = ImageType.Backdrop;
                    }

                    result.Add(imageInfo);
                }
            }

            return result;
        }
    }
}
