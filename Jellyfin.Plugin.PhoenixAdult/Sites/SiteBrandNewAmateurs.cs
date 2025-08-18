using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
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
    public class SiteBrandNewAmateurs : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}/{searchTitle.Replace(" ", string.Empty)}.html";
            var http = await HTTP.Request(searchUrl, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[contains(@class, 'item-video')]"))
                {
                    var titleNode = searchResult.SelectSingleNode("./div[@class='item-thumb']//a");
                    var titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty).Trim();
                    var sceneURL = titleNode?.GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);
                    var actorURL = Helper.Encode(searchUrl);
                    var releaseDate = searchDate.HasValue ? searchDate.Value.ToString("yyyy-MM-dd") : string.Empty;

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}|{actorURL}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            var actorURL = Helper.Decode(providerIds[3]);

            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = doc.DocumentNode.SelectSingleNode("//h3")?.InnerText.Trim();
            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='videoDetails clear']/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//ul[./li[contains(., 'Tags:')]]//a"))
            {
                var genreName = genreLink.InnerText.Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    movie.AddGenre(genreName);
                }
            }

            var actorHttp = await HTTP.Request(actorURL, cancellationToken);
            if(actorHttp.IsOK)
            {
                var actorDoc = new HtmlDocument();
                actorDoc.LoadHtml(actorHttp.Content);
                var actorName = actorDoc.DocumentNode.SelectSingleNode("//h3")?.InnerText.Trim();
                var actorPhotoNode = actorDoc.DocumentNode.SelectSingleNode("//div[@class='profile-pic']/img");
                var actorPhotoURL = actorPhotoNode?.GetAttributeValue("src0_3x", string.Empty);
                if(!actorPhotoURL.StartsWith("http"))
                {
                    actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                }

                if (!string.IsNullOrEmpty(actorName))
                {
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);

            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imageNode = doc.DocumentNode.SelectSingleNode("//meta[contains(@name, 'twitter:image')]");
                if (imageNode != null)
                {
                    var imgUrl = imageNode.GetAttributeValue("content", string.Empty);
                    if(!imgUrl.StartsWith("http"))
                    {
                        imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Primary });
                }
            }
            return images;
        }
    }
}
