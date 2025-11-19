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
    public class NetworkDerangedDollars : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => u.Contains("/session/"));

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h3[@class='mas_title']")?.InnerText.Trim(), siteNum);
                    string subSite = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split('|')[1].Trim().Replace(".com", string.Empty);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='lch']/span");
                    if (dateNode != null && DateTime.TryParse(string.Join(" ", dateNode.InnerText.Split(',').Skip(1)).Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{subSite}] {releaseDate}",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h3[@class='mas_title']")?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='mas_longdescription']")?.InnerText.Trim();
            movie.AddStudio("Deranged Dollars");

            string tagline = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split('|')[1].Trim().Replace(".com", string.Empty);
            movie.AddTag(tagline);
            movie.AddCollection(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//p[@class='tags']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(Helper.ParseTitle(genre.InnerText.Trim(), siteNum));
                }
            }

            var actorStr = detailsPageElements.SelectSingleNode("//div[@class='lch']/span")?.InnerText.Split(new[] { ',' }, 2)[0];
            if (actorStr != null)
            {
                var actors = Regex.Split(actorStr.Contains(":") ? actorStr.Split(':')[1] : actorStr, ",|&|/|And");
                var modelHttpResult1 = await HTTP.Request(Helper.GetSearchSearchURL(siteNum) + "?models", HttpMethod.Get, cancellationToken);
                var modelHttpResult2 = await HTTP.Request(Helper.GetSearchSearchURL(siteNum) + "?models/2", HttpMethod.Get, cancellationToken);
                var models = new List<HtmlNode>();
                if (modelHttpResult1.IsOK)
                {
                    models.AddRange(HTML.ElementFromString(modelHttpResult1.Content).SelectNodes("//div[@class='item']"));
                }

                if (modelHttpResult2.IsOK)
                {
                    models.AddRange(HTML.ElementFromString(modelHttpResult2.Content).SelectNodes("//div[@class='item']"));
                }

                foreach (var actor in actors)
                {
                    string actorName = Regex.Replace(actor.Trim(), @"\W", " ").Replace("Nurses", string.Empty).Replace("Nurse", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var model = models.FirstOrDefault(m => (m.InnerText.Contains(":") ? m.InnerText.Split(':')[1] : m.InnerText).Trim().Contains(actorName));
                    if (model != null)
                    {
                        actorName = (model.InnerText.Contains(":") ? model.InnerText.Split(':')[1] : model.InnerText).Trim();
                        actorPhotoUrl = Helper.GetSearchSearchURL(siteNum) + model.SelectSingleNode(".//@src").GetAttributeValue("src", string.Empty);
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='stills clearfix']//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchSearchURL(siteNum) + img.GetAttributeValue("src", string.Empty) });
                }
            }

            var scriptNode = detailsPageElements.SelectSingleNode("//div[@class='mainpic']//script");
            if (scriptNode != null)
            {
                var match = Regex.Match(scriptNode.InnerText, "'([^']*)'");
                if (match.Success)
                {
                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchSearchURL(siteNum) + match.Groups[1].Value });
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
