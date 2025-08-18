using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
    public class SiteAdultEmpire : IProviderBase
    {
        private (string, string) GetReleaseDateAndDisplayDate(HtmlNode detailsPageElements, DateTime? searchDate)
        {
            // ... (implementation from before)
            return (string.Empty, string.Empty);
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            // ... (implementation from before)
            return new List<RemoteSearchResult>(); // placeholder
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            // ... (implementation from before)
            return new MetadataResult<BaseItem>(); // placeholder
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            var splitScene = providerIds.Length > 3;

            var cookies = new CookieContainer();
            cookies.Add(new Uri(Helper.GetSearchBaseURL(siteNum)), new Cookie("ageConfirmed", "true"));

            var http = await HTTP.Request(sceneURL, cookies, cancellationToken);
            if (!http.IsOK)
            {
                return images;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            var art = new List<string>();
            var xpaths = new[]
            {
                "//div[@class='boxcover-container']/a/img/@src",
                "//div[@class='boxcover-container']/a/@href",
            };
            foreach (var xpath in xpaths)
            {
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node != null)
                {
                    art.Add(node.GetAttributeValue("src", node.GetAttributeValue("href", string.Empty)));
                }
            }

            if (splitScene)
            {
                var sceneIndex = int.Parse(providerIds[4]);
                var splitScenesXpath = $"//div[@class='row'][.//div[@class='row']][.//a[@rel='scenescreenshots']][{sceneIndex + 1}]//a/@href";
                var sceneImageNodes = doc.DocumentNode.SelectNodes(splitScenesXpath);
                if (sceneImageNodes != null)
                {
                    foreach(var node in sceneImageNodes)
                    {
                        art.Add(node.GetAttributeValue("href", string.Empty));
                    }
                }
            }
            else
            {
                var scenesXpath = "//div[@class='row'][.//div[@class='row']][.//a[@rel='scenescreenshots']]//div[@class='row']//a/@href";
                var sceneImageNodes = doc.DocumentNode.SelectNodes(scenesXpath);
                if (sceneImageNodes != null)
                {
                    foreach(var node in sceneImageNodes)
                    {
                        art.Add(node.GetAttributeValue("href", string.Empty));
                    }
                }
            }

            // This logic of downloading images to determine type can be slow.
            foreach (var imageUrl in art)
            {
                try
                {
                    var imageHttp = await HTTP.Request(imageUrl, cancellationToken, cookies);
                    if (imageHttp.IsOK)
                    {
                        using (var ms = new MemoryStream(imageHttp.ContentBytes))
                        {
                            var image = Image.FromStream(ms);
                            var imageInfo = new RemoteImageInfo { Url = imageUrl };
                            if (image.Height > image.Width)
                            {
                                imageInfo.Type = ImageType.Primary;
                            }
                            else
                            {
                                imageInfo.Type = ImageType.Backdrop;
                            }

                            images.Add(imageInfo);
                        }
                    }
                }
                catch
                {
                    // Ignore image processing errors
                }
            }

            return images;
        }
    }
}
