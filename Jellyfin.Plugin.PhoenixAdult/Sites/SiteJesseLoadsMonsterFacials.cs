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
    public class SiteJesseLoadsMonsterFacials : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> actorsDB = new Dictionary<string, List<string>>
        {
            { "Aaliyah Love", new List<string> { "We Aaliyahlove" } }, { "Addison O'Riley", new List<string> { "This Addisonoriley" } },
            { "Alana Foxx", new List<string> { "Yes. Alanafoxx" } }, { "Alexandria Devine", new List<string> { "If Alexandriadevine" } },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            int idx = 1;
            string tourPageUrl = $"{Helper.GetSearchSearchURL(siteNum)}/tour_{idx:D2}.html";
            var httpResult = await HTTP.Request(tourPageUrl, HttpMethod.Get, cancellationToken);
            while (httpResult.IsOK)
            {
                var tourPageElements = HTML.ElementFromString(httpResult.Content);
                var sceneResults = tourPageElements.SelectNodes("//table[@width='880']");
                if (sceneResults != null)
                {
                    foreach (var sceneResult in sceneResults)
                    {
                        string summary = Regex.Replace(sceneResult.SelectSingleNode(".//td[@height='105' or @height='90']")?.InnerText ?? string.Empty, @"\s+", " ").Trim();
                        string summaryId = Helper.Encode(summary);
                        string actorFirstName = summary.Split(' ')[0].Trim().ToLower();
                        string actorNameFromImg = sceneResult.SelectSingleNode(".//img[contains(@src, 'fft')]")?.GetAttributeValue("src", string.Empty).Split('_').Last().Split('.')[0].Trim().ToLower();
                        string actorName = $"{actorFirstName} {actorNameFromImg.Split(new[] { actorFirstName }, StringSplitOptions.None).Last()}".Capitalize();

                        var cleanActorName = actorsDB.FirstOrDefault(a => a.Value.Any(v => v.ToLower().Contains(actorName.ToLower()))).Key ?? actorName;
                        string titleNoFormatting = string.Join(" and ", cleanActorName);

                        string imageUrl = sceneResult.SelectSingleNode(".//img[contains(@src, 'tour')][@width='400']")?.GetAttributeValue("src", string.Empty);
                        string curId = Helper.Encode(imageUrl);

                        string releaseDate = string.Empty;
                        var dateNode = sceneResult.SelectSingleNode(".//preceding::b[contains(., 'Update')]");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split(':').Last().Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}|{Helper.Encode(string.Join("|", cleanActorName))}|{summaryId}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }

                idx++;
                tourPageUrl = $"{Helper.GetSearchSearchURL(siteNum)}/tour_{idx:D2}.html";
                httpResult = await HTTP.Request(tourPageUrl, HttpMethod.Get, cancellationToken);
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
            string sceneDate = providerIds[1];
            string[] actors = Helper.Decode(providerIds[2]).Split('|');
            string summary = Helper.Decode(providerIds[3]);

            var movie = (Movie)result.Item;
            movie.Name = $"{string.Join(" and ", actors)} from JesseLoadsMonsterFacials.com";
            movie.Overview = summary;
            movie.AddStudio("Jesse Loads Monster Facials");

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("Facial");

            foreach (var actor in actors)
            {
                if (actor != "Compilation")
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
                }
            }

            return Task.FromResult(result);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string poster = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!poster.StartsWith("http"))
            {
                poster = $"{Helper.GetSearchSearchURL(siteNum)}/{poster}";
            }

            images.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Primary });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
