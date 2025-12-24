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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkIntersec : IProviderBase
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
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'is-multiline')]/div[contains(@class, 'column')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string curId = Helper.Encode(node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty));
                    string titleNoFormatting = node.SelectSingleNode(".//div[@class='has-text-weight-bold']")?.InnerText;
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//span[contains(@class, 'tag')]");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string scenePoster = Helper.Encode(node.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{scenePoster}" } },
                        Name = $"{titleNoFormatting} [Intersec/{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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
            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/iod/{Helper.Decode(providerIds[0])}";

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectSingleNode("//div[contains(@class, 'has-text-weight-bold')]")?.InnerText;
            movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(@class, 'has-text-white-ter')][3]")?.InnerText.Trim();
            movie.AddStudio("Intersec Interactive");

            string taglineText = detailsPageElements.SelectSingleNode("//div[contains(@class, 'has-text-white-ter')][1]//a[contains(@class, 'is-dark')][last()]")?.InnerText;
            string tagline = "Intersex";
            if (taglineText != null)
            {
                if (taglineText.Contains("sexuallybroken"))
                {
                    tagline = "Sexually Broken";
                }
                else if (taglineText.Contains("infernalrestraints"))
                {
                    tagline = "Infernal Restraints";
                }
                else if (taglineText.Contains("realtimebondage"))
                {
                    tagline = "Real Time Bondage";
                }
                else if (taglineText.Contains("hardtied"))
                {
                    tagline = "Hardtied";
                }
                else if (taglineText.Contains("topgrl"))
                {
                    tagline = "Topgrl";
                }
                else if (taglineText.Contains("sensualpain"))
                {
                    tagline = "Sensual Pain";
                }
                else if (taglineText.Contains("paintoy"))
                {
                    tagline = "Pain Toy";
                }
                else if (taglineText.Contains("renderfiend"))
                {
                    tagline = "Renderfiend";
                }
                else if (taglineText.Contains("hotelhostages"))
                {
                    tagline = "Hotel Hostages";
                }
            }

            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'has-text-white-ter')][1]//span[contains(@class, 'is-dark')][1]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("BDSM");
            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'has-text-white-ter')][1]//a[contains(@class, 'is-dark')][position() < last()]");
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
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/iod/{Helper.Decode(providerIds[0])}";
            string scenePoster = Helper.Decode(providerIds[1]);
            images.Add(new RemoteImageInfo { Url = scenePoster });

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//video-js");
            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("poster", string.Empty) });
            }

            var imageNodes = detailsPageElements.SelectNodes("//figure/img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", string.Empty) });
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
