using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class NetworkHighTechVR : IProviderBase
    {
        private static readonly Dictionary<string, Dictionary<string, string>> xPathMap = new Dictionary<string, Dictionary<string, string>>
        {
            {"SexBabesVR", new Dictionary<string, string> {
                {"date", "//div[contains(@class, 'video-detail__description--container')]/div[last()]"},
                {"summary", "//div[contains(@class, 'video-detail')]/div/p"},
                {"tags", "//a[contains(@class, 'tag')]"},
                {"actor", "//div[@class='video-detail__description--author']//a"},
                {"actorPhoto", "//img[contains(@class, 'cover-picture')]"},
                {"images", "//a[contains(@data-fancybox, 'gallery')]//img/@src"},
                {"poster", "//dl8-video"},
            }                        },
            {"StasyQ VR", new Dictionary<string, string> {
                {"date", "//div[@class='video-meta-date']"},
                {"summary", "//div[@class='video-info']/p"},
                {"tags", "//div[contains(@class, 'my-2 lh-lg')]//a"},
                {"actor", "//div[@class='model-one-inner js-trigger-lazy-item']//a"},
                {"actorPhoto", "//div[contains(@class, 'model-one-inner')]//img"},
                {"images", "//div[contains(@class, 'video-gallery')]//div//figure//a/@href"},
                {"poster", "//div[@class='splash-screen fullscreen-message is-visible'] | //dl8-video"},
            }                        },
            {"RealJamVR", new Dictionary<string, string> {
                {"date", "//div[@class='ms-4 text-nowrap']"},
                {"summary", "//div[@class='opacity-75 my-2']"},
                {"tags", "//div[contains(@class, 'my-2 lh-lg')]//a"},
                {"actor", "//div[@class='scene-view mx-auto']/a"},
                {"actorPhoto", "//div[@class='col-12 col-lg-4 pe-lg-0']//img"},
                {"images", "//img[@class='img-thumb']/@src"},
                {"poster", "//div[@class='splash-screen fullscreen-message is-visible'] | //dl8-video"},
            }                        },
        };

        private string Slugify(string phrase)
        {
            string str = phrase.ToLowerInvariant();
            str = System.Text.Encoding.ASCII.GetString(System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(str));
            str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s-]", string.Empty);
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim();
            str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s", "+");
            return str;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string encoded = Slugify(searchTitle);
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + encoded;
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = HTML.ElementFromString(httpResult.Content);
            string titleNoFormatting = searchResults.SelectSingleNode("//h1")?.InnerText.Trim();
            string curId = encoded;
            string releaseDate = string.Empty;
            foreach (var key in xPathMap.Keys)
            {
                var dateNode = searchResults.SelectSingleNode(xPathMap[key]["date"]);
                if (dateNode != null)
                {
                    if (DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    break;
                }
            }

            result.Add(new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                SearchProviderName = Plugin.Instance.Name,
            });

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}{sceneID[0].Split('|')[0]}";
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            string siteName = Helper.GetSearchSiteName(siteNum);
            var siteXPath = xPathMap[siteName];

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            var summaryNode = detailsPageElements.SelectSingleNode(siteXPath["summary"]);
            if(summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio(siteName);
            var taglineNode = detailsPageElements.SelectSingleNode("//title");
            if (taglineNode != null)
            {
                string tagline = taglineNode.InnerText.Trim();
                if (tagline.Contains("|"))
                {
                    tagline = tagline.Split('|')[1].Trim();
                }
                else if (tagline.Contains("-"))
                {
                    tagline = tagline.Split('-')[0].Trim();
                }

                movie.AddTag(tagline);
                }

            var dateNode = detailsPageElements.SelectSingleNode(siteXPath["date"]);
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes(siteXPath["tags"]);
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes(siteXPath["actor"]);
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        var photoNode = actorPage.SelectSingleNode(siteXPath["actorPhoto"]);
                        if(photoNode != null)
                        {
                            actorPhotoUrl = photoNode.GetAttributeValue("src", string.Empty).Split('?')[0];
                        }
                    }
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}{sceneID[0].Split('|')[0]}";
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            string siteName = Helper.GetSearchSiteName(siteNum);
            var siteXPath = xPathMap[siteName];

            var imageNodes = detailsPageElements.SelectNodes(siteXPath["images"]);
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty).Split('?')[0];
                    if (imageUrl.StartsWith("http"))
                    {
                        images.Add(new RemoteImageInfo { Url = imageUrl });
                    }
                }
            }

            var posterNode = detailsPageElements.SelectSingleNode(siteXPath["poster"]);
            if (posterNode != null)
            {
                string imageUrl = posterNode.GetAttributeValue("poster", string.Empty);
                if(string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = posterNode.GetAttributeValue("style", string.Empty).Split(new[] { "url(" }, StringSplitOptions.None).Last().Split(')')[0];
                }

                images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
