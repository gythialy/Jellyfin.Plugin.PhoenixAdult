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
    public class SiteCumLouder : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string url = $"{Helper.GetSearchSearchURL(siteNum)}%22{Uri.EscapeDataString(searchTitle)}%22";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='listado-escenas listado-busqueda']//div[@class='medida']/a");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode(".//h2")?.InnerText.Trim();
                    string curId = Helper.Encode(Helper.GetSearchBaseURL(siteNum) + node.GetAttributeValue("href", string.Empty));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [CumLouder]",
                        SearchProviderName = Plugin.Instance.Name,
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@id='content-more-less']/p")?.InnerText.Trim();
            movie.AddStudio("CumLouder");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='added']");
            if (dateNode != null)
            {
                var dateParts = dateNode.InnerText.Trim().Split(' ');
                if (dateParts.Length > 2 && int.TryParse(dateParts[1], out var timeNumber))
                {
                    string timeFrame = dateParts[2];
                    var today = DateTime.Now;
                    if (timeFrame.StartsWith("minute"))
                    {
                        movie.PremiereDate = today;
                    }
                    else if (timeFrame.StartsWith("hour"))
                    {
                        movie.PremiereDate = today.AddHours(-timeNumber);
                    }
                    else if (timeFrame.StartsWith("day"))
                    {
                        movie.PremiereDate = today.AddDays(-timeNumber);
                    }
                    else if (timeFrame.StartsWith("week"))
                    {
                        movie.PremiereDate = today.AddDays(-7 * timeNumber);
                    }
                    else if (timeFrame.StartsWith("month"))
                    {
                        movie.PremiereDate = today.AddMonths(-timeNumber);
                    }
                    else if (timeFrame.StartsWith("year"))
                    {
                        movie.PremiereDate = today.AddYears(-timeNumber);
                    }

                    if (movie.PremiereDate.HasValue)
                    {
                        movie.ProductionYear = movie.PremiereDate.Value.Year;
                    }
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//ul[@class='tags']/li/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[@class='pornstar-link']");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNode = detailsPageElements.SelectSingleNode("//div[@class='box-video box-video-html5']/video");
            if (imageNode != null)
            {
                images.Add(new RemoteImageInfo { Url = imageNode.GetAttributeValue("lazy", string.Empty), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
