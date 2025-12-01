using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class Network5kporn : IProviderBase
    {
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "nats", "MC4wLjMuNTguMC4wLjAuMC4w" } };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(httpResult.Content);
            var html = json["html"]?.ToString();

            if (string.IsNullOrEmpty(html))
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'epwrap')]");
            if (nodes == null)
            {
                return result;
            }

            foreach (var node in nodes)
            {
                var titleNode = node.SelectSingleNode(".//h3[contains(@class, 'ep-title')]");
                var sceneUrlNode = node.SelectSingleNode(".//a");
                var imageNode = node.SelectSingleNode(".//img[contains(@class, 'stack')]");
                if (titleNode != null && sceneUrlNode != null)
                {
                    string titleNoFormatting = titleNode.InnerText.Trim();
                    string sceneUrl = sceneUrlNode.GetAttributeValue("href", string.Empty);
                    string imageUrl = imageNode?.GetAttributeValue("src", string.Empty);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDateStr = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDateStr}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = imageUrl,
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : string.Empty;

            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken, _cookies);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;

            var titleNode = detailsPageElements.SelectSingleNode("//title");
            if (titleNode != null)
            {
                movie.Name = titleNode.InnerText.Split('|')[0].Trim();
            }

            var summaryNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'video-summary')]//p[@class='']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("5Kporn");

            string tagline;
            if (sceneUrl.Contains("5KT"))
            {
                tagline = "5Kteens";
            }
            else
            {
                tagline = Helper.GetSearchSiteName(siteNum);
            }

            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//h5[contains(., 'Published')]");
            if (dateNode != null)
            {
                string dateText = dateNode.InnerText.Replace("Published:", string.Empty).Trim();
                if (DateTime.TryParse(dateText, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//h5[contains(., 'Starring')]/a");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    string actorUrl = actorNode.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;

                    if (!string.IsNullOrEmpty(actorUrl))
                    {
                        var actorPageElements = await HTML.ElementFromURL(actorUrl, cancellationToken, _cookies);
                        if (actorPageElements != null)
                        {
                            var imgNode = actorPageElements.SelectSingleNode("//img[@class='model-image']");
                            if (imgNode != null)
                            {
                                actorPhotoUrl = imgNode.GetAttributeValue("src", string.Empty);
                            }
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);

            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken, _cookies);
            if (detailsPageElements == null)
            {
                return images;
            }

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'gal')]//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Backdrop });
                    }
                }
            }

            for (int i = 1; i <= 2; i++)
            {
                string photoPageUrl = $"{sceneUrl}/photoset?page={i}";
                var photoPageElements = await HTML.ElementFromURL(photoPageUrl, cancellationToken, _cookies);
                if (photoPageElements != null)
                {
                    var photoNodes = photoPageElements.SelectNodes("//img[@class='card-img-top']");
                    if (photoNodes != null)
                    {
                        foreach (var img in photoNodes)
                        {
                            string imageUrl = img.GetAttributeValue("src", string.Empty);
                            if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.Contains("full"))
                            {
                                images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Backdrop });
                            }
                        }
                    }
                }
            }

            // Set first image as primary
            if (images.Any())
            {
                images[0].Type = ImageType.Primary;
            }

            return images;
        }
    }
}
