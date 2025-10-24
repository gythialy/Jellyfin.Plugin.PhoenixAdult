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
    public class NetworkPuffy : IProviderBase
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
            var searchNodes = searchPageElements.SelectNodes("//div[@style='position:relative; background:black;']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//a");
                    string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty);
                    string subSiteSrc = node.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);
                    string subSite = "Wet and Pissy";
                    if (subSiteSrc != null)
                    {
                        if (subSiteSrc.Contains("weliketosuck"))
                        {
                            subSite = "We Like To Suck";
                        }

                        if (subSiteSrc.Contains("wetandpuffy"))
                        {
                            subSite = "Wet and Puffy";
                        }

                        if (subSiteSrc.Contains("simplyanal"))
                        {
                            subSite = "Simply Anal";
                        }

                        if (subSiteSrc.Contains("eurobabefacials"))
                        {
                            subSite = "Euro Babe Facials";
                        }
                    }

                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [Puffy Network/{subSite}]",
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
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div/section[1]/div[2]/h2/span")?.InnerText.Trim();

            string allSummary = detailsPageElements.SelectSingleNode("//div/section[3]/div[2]")?.InnerText.Trim();
            string tagsSummary = detailsPageElements.SelectSingleNode("//div/section[3]/div[2]/p")?.InnerText.Trim();
            if (allSummary != null && tagsSummary != null)
            {
                movie.Overview = allSummary.Replace(tagsSummary, string.Empty).Split(new[] { "Show more..." }, StringSplitOptions.None)[0].Trim();
            }

            movie.AddStudio("Puffy Network");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div/section[2]/dl/dt[2]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Released on:", string.Empty), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div/section[3]/div[2]/p/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div/section[2]/dl/dd[1]/a");
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
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    if (!actorPageUrl.StartsWith("http"))
                    {
                        actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actorPageUrl;
                    }

                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div/section[1]/div/div[1]/img")?.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                        {
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                        }
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            string tagline = item.Tags.FirstOrDefault() ?? string.Empty;

            string twitterBg = null;
            string cover = sceneUrl.Split(new[] { "-video-" }, StringSplitOptions.None)[1];
            if (tagline == "Wet and Pissy")
            {
                twitterBg = $"https://media.wetandpissy.com/videos/video-{cover}cover/hd.jpg";
            }
            else if (tagline == "We Like To Suck")
            {
                twitterBg = $"https://media.weliketosuck.com/videos/video-{cover}cover/hd.jpg";
            }
            else if (tagline == "Wet and Puffy")
            {
                twitterBg = $"https://media.wetandpuffy.com/videos/video-{cover}cover/hd.jpg";
            }
            else if (tagline == "Simply Anal")
            {
                twitterBg = $"https://media.simplyanal.com/videos/video-{cover}cover/hd.jpg";
            }
            else if (tagline == "Euro Babe Facials")
            {
                twitterBg = $"https://media.eurobabefacials.com/videos/video-{cover}cover/hd.jpg";
            }

            if (twitterBg != null)
            {
                images.Add(new RemoteImageInfo { Url = twitterBg, Type = ImageType.Primary });
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@id, 'pics')]//@src");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", string.Empty) });
                }
            }

            return images;
        }
    }
}
