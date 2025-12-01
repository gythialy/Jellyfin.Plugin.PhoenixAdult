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
    public class NetworkThickCash : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string modelId;
            string sceneTitle;
            try
            {
                modelId = string.Join("-", searchTitle.Split(' ').Take(2));
                sceneTitle = string.Join(" ", searchTitle.Split(' ').Skip(2));
            }
            catch
            {
                modelId = searchTitle.Split(' ').First();
                sceneTitle = string.Join(" ", searchTitle.Split(' ').Skip(1));
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}{modelId}.html";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchResults.SelectNodes("//div[@class='updateBlock clear']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode(".//h3")?.InnerText.Trim();
                    string description = node.SelectSingleNode(".//p")?.InnerText.Trim();
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//h4");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split(':').Last().Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string poster = node.SelectSingleNode(".//@src")?.GetAttributeValue("src", string.Empty);
                    string subSite = Helper.GetSearchSiteName(siteNum);

                    string curId = Helper.Encode(titleNoFormatting);
                    string descriptionId = Helper.Encode(description);
                    string posterId = Helper.Encode(poster);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{descriptionId}|{releaseDate}|{posterId}" } },
                        Name = $"{titleNoFormatting} [Thick Cash/{subSite}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            return result;
        }

        public Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string[] providerIds = sceneID[0].Split('|');
            string sceneTitle = Helper.Decode(providerIds[0]);
            string sceneDescription = Helper.Decode(providerIds[1]);
            string sceneDate = providerIds[2];

            var movie = (Movie)result.Item;
            movie.Name = sceneTitle;
            movie.Overview = sceneDescription;
            movie.AddStudio("Thick Cash");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            string subSite = Helper.GetSearchSiteName(siteNum).ToLower();
            if (subSite == "family lust")
            {
                movie.AddGenre("Family Roleplay");
            }
            else if (subSite == "over 40 handjobs")
            {
                movie.AddGenre("MILF");
                movie.AddGenre("Handjob");
            }
            else if (subSite == "ebony tugs")
            {
                movie.AddGenre("Ebony");
                movie.AddGenre("Handjob");
            }
            else if (subSite == "teen tugs")
            {
                movie.AddGenre("Teen");
                movie.AddGenre("Handjob");
            }

            if (DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            return Task.FromResult(result);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string scenePoster = Helper.Decode(sceneID[0].Split('|')[3]);
            images.Add(new RemoteImageInfo { Url = scenePoster, Type = ImageType.Primary });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
