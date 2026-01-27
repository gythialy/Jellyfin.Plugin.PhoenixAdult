using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text.Json;
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
using Jellyfin.Data.Enums;

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

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}&type=episodes";

            do
            {
                var currentUri = new Uri(searchUrl);
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                if (!httpResult.IsOK)
                {
                    break;
                }

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(httpResult.Content);
                var searchResults = htmlDoc.DocumentNode.SelectNodes("//a[contains(@class, \"video-card\")]");

                if (searchResults != null)
                {
                    foreach (var node in searchResults)
                    {
                        var titleNode = node.SelectSingleNode(".//h3");
                        string titleNoFormatting = titleNode?.InnerText.Trim();
                        string sceneUrl = node.GetAttributeValue("href", string.Empty);
                        string episodeId = node.SelectSingleNode(".//span[@class='video-title']")?.InnerText.Split('#').Last().Trim();
                        string subsite = node.SelectSingleNode(".//span[@class='badge badge-brand']")?.InnerText.Trim().Replace("PF", "PornFidelity").Replace("TF", "TeenFidelity").Replace("KM", "Kelly Madison");
                        string releaseDate = string.Empty;
                        var dateNode = node.SelectSingleNode(".//time");
                        if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MM/dd/yy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        string displayDate = !string.IsNullOrEmpty(releaseDate) ? releaseDate : string.Empty;
                        string curId = Helper.Encode($"{sceneUrl}|{releaseDate}");

                        string imgUrl = node.SelectSingleNode(".//img[contains(@class, 'video-thumbnail')]")?.GetAttributeValue("src", string.Empty);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curId } },
                            Name = $"{titleNoFormatting} [{subsite}] {displayDate}",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = imgUrl,
                        });
                    }
                }

                searchUrl = null;
                var nextNode = htmlDoc.DocumentNode.SelectSingleNode("//a[@rel='next']");
                if (nextNode != null)
                {
                    var nextUrl = nextNode.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(nextUrl))
                    {
                        searchUrl = new Uri(currentUri, nextUrl).ToString();
                        searchUrl = System.Net.WebUtility.HtmlDecode(searchUrl);
                    }
                }

            } while (!string.IsNullOrEmpty(searchUrl) && !cancellationToken.IsCancellationRequested);

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string[] providerIds = Helper.Decode(sceneID[0]).Split('|');
            string sceneUrl = providerIds[0];
            string sceneDate = providerIds[1];

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            string title = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[contains(@class, 'title')]")?.InnerText.Trim(), siteNum);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            HtmlNodeCollection actorNodes;
            try
            {
                movie.Name = title;
                movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(., 'Episode Summary')]/p")?.InnerText.Trim();
                movie.AddStudio("Kelly Madison");
                actorNodes = detailsPageElements.SelectNodes("//p[contains(., 'Starring')]//a[contains(@href, '/models/')]");
            }
            catch
            {
                movie.Name = title;
                movie.AddStudio("Kelly Madison");
                actorNodes = detailsPageElements.SelectNodes(".//a[contains(@href, '/models/')]");
            }

            string tagline = "Kelly Madison";
            if (movie.Name.ToLower().Contains("teenfidelity"))
            {
                tagline = "TeenFidelity";
            }
            else if (movie.Name.ToLower().Contains("kelly madison"))
            {
                tagline = "Kelly Madison";
            }
            else
            {
                tagline = Helper.GetSearchSiteName(siteNum);
            }

            movie.AddStudio(tagline);
            movie.AddCollection(tagline);

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
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[contains(@class, 'one')]//@src")?.GetAttributeValue("src", string.Empty);
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneId = Helper.Decode(sceneID[0]).Split('|')[0].Split('/').Last();
            images.Add(new RemoteImageInfo { Url = $"https://tour-content-cdn.kellymadisonmedia.com/episode/poster_image/{sceneId}/poster.jpg", Type = ImageType.Primary });
            images.Add(new RemoteImageInfo { Url = $"https://tour-content-cdn.kellymadisonmedia.com/episode/episode_thumb_image_1/{sceneId}/01.jpg", Type = ImageType.Backdrop });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
