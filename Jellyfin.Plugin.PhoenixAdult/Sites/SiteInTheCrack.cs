using System;
using System.Collections.Generic;
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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteInTheCrack : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = Regex.Replace(searchTitle.Split(' ').First(), @"\D", "");
            if (!string.IsNullOrEmpty(sceneId))
                searchTitle = string.Join(" ", searchTitle.Split(' ').Skip(1));

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle[0]}";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var searchNodes = searchPageElements.SelectNodes("//ul[@class='collectionGridLayout']/li");
            if (searchNodes != null)
            {
                var searchNode = searchNodes.FirstOrDefault(n => n.SelectSingleNode(".//span")?.InnerText.Trim().ToLower() == searchTitle.ToLower());
                if (searchNode != null)
                {
                    string modelLink = searchNode.SelectSingleNode(".//a")?.GetAttributeValue("href", "");
                    var modelHttp = await HTTP.Request(Helper.GetSearchBaseURL(siteNum) + modelLink, HttpMethod.Get, cancellationToken);
                    if (modelHttp.IsOK)
                    {
                        var modelPageElements = await HTML.ElementFromString(modelHttp.Content, cancellationToken);
                        var modelNodes = modelPageElements.SelectNodes("//ul[@class='Models']/li");
                        if(modelNodes != null)
                        {
                            foreach (var node in modelNodes)
                            {
                                var titleNode = node.SelectSingleNode(".//figure/p[1]");
                                string titleNoFormatting = titleNode?.InnerText.Replace("Collection: ", "").Trim();
                                string releaseDate = string.Empty;
                                var dateNode = node.SelectSingleNode(".//figure/p[2]");
                                if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Release Date:", "").Trim(), out var parsedDate))
                                    releaseDate = parsedDate.ToString("yyyy-MM-dd");

                                string curId = Helper.Encode(node.SelectSingleNode(".//a")?.GetAttributeValue("href", ""));
                                result.Add(new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{Helper.Encode(titleNoFormatting)}|{releaseDate}" } },
                                    Name = $"{titleNoFormatting} {releaseDate} [{Helper.GetSearchSiteName(siteNum)}]",
                                    SearchProviderName = Plugin.Instance.Name
                                });
                            }
                        }
                    }
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
            if (!sceneUrl.StartsWith("http"))
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h2//span")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@id='CollectionDescription']")?.InnerText.Trim();
            movie.AddStudio("InTheCrack");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });
            movie.AddGenre("Solo");

            string sceneDate = providerIds[3];
            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            string actorStr = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split('#')[1];
            if(actorStr != null)
            {
                actorStr = Regex.Replace(actorStr, @"\d", "").Trim().Replace(",", "&");
                var actors = actorStr.Split('&');
                foreach(var actor in actors)
                    result.People.Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var styleNode = detailsPageElements.SelectSingleNode("//style");
            if(styleNode != null)
            {
                var match = Regex.Match(styleNode.InnerText, "'(.*)'");
                if (match.Success)
                    images.Add(new RemoteImageInfo { Url = $"{Helper.GetSearchBaseURL(siteNum).Trim()}{match.Groups[1].Value}", Type = ImageType.Primary });
            }

            return images;
        }
    }
}
