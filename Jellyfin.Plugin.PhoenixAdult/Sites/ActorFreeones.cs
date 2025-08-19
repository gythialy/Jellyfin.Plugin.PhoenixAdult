using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class ActorFreeones : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string actorName, DateTime? actorDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            var url = Helper.GetSearchSearchURL(siteNum) + actorName;
            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            foreach (var actorNode in actorData.SelectNodesSafe("//div[contains(@class, 'grid-item')]"))
            {
                var actorURL = new Uri(Helper.GetSearchBaseURL(siteNum) + actorNode.SelectSingleText(".//a/@href").Replace("/feed", "/bio", StringComparison.OrdinalIgnoreCase));
                string curID = Helper.Encode(actorURL.AbsolutePath),
                    name = actorNode.SelectSingleText(".//p/@title"),
                    imageURL = actorNode.SelectSingleText(".//img/@src");

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = name,
                    ImageUrl = imageURL,
                };

                result.Add(res);
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Person(),
                HasMetadata = true,
            };

            var actorURL = Helper.Decode(sceneID[0]);
            if (!actorURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                actorURL = Helper.GetSearchBaseURL(siteNum) + actorURL;
            }
            Logger.Info($"actorURL: {actorURL}");

            var actorData = await HTML.ElementFromURL(actorURL, cancellationToken).ConfigureAwait(false);
            Logger.Info($"actorData: {actorData}");

            result.Item.ExternalId = actorURL;

            string name = actorData.OwnerDocument.DocumentNode.SelectSingleNode("//title").InnerText.Split('|')[0].Replace(" bio", string.Empty).Trim();
            Logger.Info($"name: {name}");
            string aliases = actorData.SelectSingleText("//li[span[text()='Aliases:']]//span[contains(@class, 'font-size-xs')]")?.Trim();
            Logger.Info($"aliases: {aliases}");
            result.Item.Name = name;
            result.Item.OriginalTitle = name + ", " + aliases;
            string overview = actorData.SelectSingleText("//div[@data-test='biography']");
            Logger.Info($"overview: {overview}");
            result.Item.Overview = overview?.Trim() ?? string.Empty;

            var actorDate = actorData.SelectSingleText("//li[span[text()='Date of birth:']]//span[@data-test='link_span_dateOfBirth']")?.Trim();
            Logger.Info($"actorDate: {actorDate}");
            if (DateTime.TryParseExact(actorDate, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var bornPlaceNodes = actorData.SelectNodes("//li[span[text()='Place of birth:']]//span[@data-test='link_span_placeOfBirth']");
            if (bornPlaceNodes != null)
            {
                var bornPlaceList = bornPlaceNodes.Select(n => n.InnerText.Trim()).ToList();
                result.Item.ProductionLocations = new string[] { string.Join(", ", bornPlaceList) };
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var actorURL = Helper.Decode(sceneID[0]);
            if (!actorURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                actorURL = Helper.GetSearchBaseURL(siteNum) + actorURL;
            }

            var actorData = await HTML.ElementFromURL(actorURL, cancellationToken).ConfigureAwait(false);

            var img = actorData.SelectSingleText("//div[contains(@class, 'image-container')]//a/img/@src");
            if (!string.IsNullOrEmpty(img))
            {
                result.Add(new RemoteImageInfo
                {
                    Type = ImageType.Primary,
                    Url = img,
                });
            }

            return result;
        }
    }
}
