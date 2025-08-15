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
    public class SiteJacquieEtMichel : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> scenes = new Dictionary<string, List<string>>
        {
            {"4554/ibiza-1-crumb-in-the-mouth", new List<string> {"Alexis Crystal", "Cassie Del Isla", "Dorian Del Isla"}},
            {"4558/orgies-in-ibiza-2-lucys-surprise", new List<string> {"Alexis Crystal", "Cassie Del Isla", "Lucy Heart", "Dorian Del Isla", "James Burnett Klein", "Vlad Castle"}},
            {"4564/orgies-in-ibiza-3-overheated-orgy-by-the-pool", new List<string> {"Alexis Crystal", "Cassie Del Isla", "Lucy Heart", "Dorian Del Isla", "James Burnett Klein", "Vlad Castle"}},
            {"4570/orgies-in-ibiza-4-orgy-with-a-bang-for-the-last-night", new List<string> {"Alexis Crystal", "Cassie Del Isla", "Lucy Heart", "Dorian Del Isla", "James Burnett Klein", "Vlad Castle"}},
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? "", out var id))
                sceneId = id.ToString();

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                var searchNodes = searchPageElements.SelectNodes("//a[@class='content-card content-card--video']");
                if (searchNodes != null)
                {
                    foreach (var node in searchNodes)
                    {
                        var titleNode = node.SelectSingleNode(".//h2[@class='content-card__title']");
                        string titleNoFormatting = titleNode?.InnerText.Trim();
                        string curId = Helper.Encode(node.GetAttributeValue("href", ""));
                        string releaseDate = string.Empty;
                        var dateNode = node.SelectSingleNode(".//div[@class='content-card__date']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Added on", "").Trim(), out var parsedDate))
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name
                        });
                    }
                }
            }

            if (sceneId != null)
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/en/content/{sceneId}";
                httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1[@class='content-detail__title']")?.InnerText.Trim();
                    string curId = Helper.Encode(sceneUrl);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name
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
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1[@class='content-detail__title']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='content-detail__description']")?.InnerText.Trim();
            movie.AddStudio("Jacquie Et Michel TV");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            var dateNode = detailsPageElements.SelectNodes("//div[@class='content-detail__infos__row']//p[@class='content-detail__description content-detail__description--link']")?.LastOrDefault();
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='content-detail__row']//li[@class='content-detail__tag']");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    string genreName = genre.InnerText.Replace(",", "").Trim();
                    if (genreName == "Sodomy")
                        genreName = "Anal";
                    movie.AddGenre(genreName);
                }
            }
            movie.AddGenre("French porn");

            var scene = scenes.FirstOrDefault(s => sceneUrl.Contains(s.Key));
            if(scene.Key != null)
            {
                foreach(var actor in scene.Value)
                    result.People.Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
            }

            var directorNode = detailsPageElements.SelectSingleNode("//span[@class='director']");
            if(directorNode != null)
                result.People.Add(new PersonInfo { Name = directorNode.InnerText.Replace("Director :", "").Trim(), Type = PersonKind.Director });

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

            var posterNode = detailsPageElements.SelectSingleNode("//video");
            if(posterNode != null)
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("poster", ""), Type = ImageType.Primary });

            return images;
        }
    }
}
