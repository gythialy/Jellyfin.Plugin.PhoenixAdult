using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class NetworkAuntJudys : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='update_details']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode("./div/a");
                    if (titleNode != null)
                    {
                        string titleNoFormatting = Helper.ParseTitle(titleNode.InnerText.Trim(), siteNum);
                        string sceneUrl = titleNode.GetAttributeValue("href", string.Empty);
                        if (sceneUrl.Contains("_vids.html", StringComparison.OrdinalIgnoreCase))
                        {
                            string curId = Helper.Encode(sceneUrl);
                            string releaseDate = string.Empty;
                            var dateNode = node.SelectSingleNode(".//div[@class='cell update_date']");
                            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }
                            else if (searchDate.HasValue)
                            {
                                releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                Name = $"{titleNoFormatting} [Aunt Judy's] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
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
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//span[@class='title_bar_hilite']")?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode("//span[@class='update_description']")?.InnerText.Trim();

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='gallery_info']//div[@class='cell update_date']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//span[@class='update_tags']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(Helper.ParseTitle(genre.InnerText.Trim(), siteNum));
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//p/span[@class='update_models']/a");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    string actorUrl = actorNode.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    if (!string.IsNullOrEmpty(actorUrl))
                    {
                        var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorPage = HTML.ElementFromString(actorHttp.Content);
                            actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='cell_top cell_thumb']//@src0_1x")?.GetAttributeValue("src0_1x", string.Empty);
                            if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                            {
                                actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                            }
                        }
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(new List<RemoteImageInfo>());
        }
    }
}
