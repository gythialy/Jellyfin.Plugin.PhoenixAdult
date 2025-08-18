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
    public class NetworkKellyMadison : IProviderBase
    {
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "nats", "MC4wLjMuNTguMC4wLjAuMC4w" } };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(Regex.Replace(searchTitle.Split(' ').First(), @"^e(?=\d+$)", string.Empty, RegexOptions.IgnoreCase), out _))
            {
                sceneId = Regex.Replace(searchTitle.Split(' ').First(), @"^e(?=\d+$)", string.Empty, RegexOptions.IgnoreCase);
                searchTitle = searchTitle.Replace(searchTitle.Split(' ').First(), string.Empty).Trim();
            }

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = await HTML.ElementsFromJSON(httpResult.Content, "html", cancellationToken);
            if (searchResults == null)
            {
                return result;
            }

            foreach (var node in searchResults)
            {
                var titleNode = node.SelectSingleNode(".//a[@class='text-km'] | .//a[@class='text-pf'] | .//a[@class='text-tf']");
                string titleNoFormatting = titleNode?.InnerText.Trim();
                string sceneUrl = titleNode?.GetAttributeValue("href", string.Empty);
                string episodeId = node.SelectSingleNode(".//span[@class='card-footer-item'][last()]")?.InnerText.Split('#').Last().Trim();
                string curId = Helper.Encode(sceneUrl);
                string releaseDate = string.Empty;
                var dateNode = node.SelectSingleNode(".//span[.//i[@class='far fa-calendar-alt']]");
                if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                {
                    releaseDate = parsedDate.ToString("yyyy-MM-dd");
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}|{Helper.Encode(titleNoFormatting)}|{Helper.Encode(node.OuterHtml)}" } },
                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                    SearchProviderName = Plugin.Instance.Name,
                });
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
            string sceneDate = providerIds[2];
            string title = providerIds.Length > 3 ? Helper.Decode(providerIds[3]) : null;
            var searchResult = providerIds.Length > 4 ? await HTML.ElementFromString(Helper.Decode(providerIds[4]), cancellationToken) : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            HtmlNodeCollection actorNodes;
            try
            {
                movie.Name = detailsPageElements.SelectSingleNode("//div[@class='level-left']")?.InnerText.Trim();
                movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='column is-three-fifths']")?.InnerText.Replace("Episode Summary", string.Empty).Trim();
                movie.AddStudio("Kelly Madison Productions");
                actorNodes = detailsPageElements.SelectNodes("//a[@class='is-underlined']");
            }
            catch
            {
                movie.Name = title;
                movie.AddStudio("Kelly Madison Productions");
                actorNodes = searchResult.SelectNodes(".//a[contains(@href, '/models/')]");
            }

            string tagline = "Kelly Madison";
            if (movie.Name.ToLower().Contains("teenfidelity"))
            {
                tagline = "TeenFidelity";
            }

            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//li[.//i[@class='fas fa-calendar']]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split(':').Last().Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("Hardcore");
            movie.AddGenre("Heterosexual");

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
                    string actorName = actor.InnerText;
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[contains(@class, 'one')]//@src")?.GetAttributeValue("src", string.Empty);
                    }
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneId = Helper.Decode(sceneID[0].Split('|')[0]).Split('/').Last();
            images.Add(new RemoteImageInfo { Url = $"https://tour-cdn.kellymadisonmedia.com/content/episode/poster_image/{sceneId}/poster.jpg", Type = ImageType.Primary });
            images.Add(new RemoteImageInfo { Url = $"https://tour-cdn.kellymadisonmedia.com/content/episode/episode_thumb_image_1/{sceneId}/1.jpg" });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
