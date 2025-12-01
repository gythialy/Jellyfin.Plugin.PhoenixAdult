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
    public class SiteMissaX : IProviderBase
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
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='updateItem'] | //div[@class='photo-thumb video-thumb'] | //div[@class='update_details']"))
                {
                    var titleNoFormatting = Helper.ParseTitle(searchResult.SelectSingleNode(".//h4//a | .//p[@class='thumb-title'] | ./a[./preceding-sibling::a]").InnerText.Trim(), siteNum);
                    var sceneURL = searchResult.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);

                    var dateNode = searchResult.SelectSingleNode(".//span[@class='update_thumb_date'] | .//span[@class='date'] | .//div[contains(@class, 'updateDetails')]/p/span[2] | .//div[contains(@class, 'update_date')]");
                    var releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var image = searchResult.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);
                    if (image != null && !image.StartsWith("http"))
                    {
                        image = Helper.GetSearchBaseURL(siteNum) + image;
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = image,
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
            movie.Name = Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//span[@class='update_title'] | //p[@class='raiting-section__title']").InnerText.Trim(), siteNum);
            var summaryNode = doc.DocumentNode.SelectSingleNode("//span[@class='latest_update_description'] | //p[contains(@class, 'text')]");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Replace("Includes:", string.Empty).Replace("Synopsis:", string.Empty).Trim();
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='update_date'] | //span[contains(@class, 'availdate')] | //p[@class='dvd-scenes__data']");
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Split('|').Length > 1 ? dateNode.InnerText.Split('|')[1] : dateNode.InnerText;
                if (DateTime.TryParse(dateText.Replace("Available to Members Now", string.Empty).Replace("Added:", string.Empty).Trim(), out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//div[contains(@class, 'update_block')]/span[@class='tour_update_models']//a | //p[@class='dvd-scenes__data'][1]//a"))
            {
                var actorName = actorLink.InnerText.Trim();
                var actorPhotoURL = string.Empty;
                var actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(actorPageURL))
                {
                    var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorDoc = new HtmlDocument();
                        actorDoc.LoadHtml(actorHttp.Content);
                        var actorPhotoNode = actorDoc.DocumentNode.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')]");
                        if (actorPhotoNode != null)
                        {
                            actorPhotoURL = actorPhotoNode.GetAttributeValue("src0_1x", string.Empty);
                        }

                        if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                        {
                            actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                        }
                    }
                }

                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//span[contains(@class, 'update_tags')]//a | //p[@class='dvd-scenes__data'][2]//a"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
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

                var imageNodes = doc.DocumentNode.SelectNodes("//img[contains(@class, 'update_thumb')]");
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src0_4x", img.GetAttributeValue("src0_1x", string.Empty));
                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            if (!imgUrl.StartsWith("http"))
                            {
                                imgUrl = Helper.GetSearchBaseURL(siteNum) + "/" + imgUrl;
                            }

                            images.Add(new RemoteImageInfo { Url = imgUrl.Split('?')[0] });
                        }
                    }
                }
            }

            if (images.Count > 0)
            {
                images[0].Type = ImageType.Primary;
            }

            return images;
        }
    }
}
