using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class NetworkPornPros : IProviderBase
    {
        private static readonly Dictionary<string, string[]> GenresDB = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Anal4K", new[] { "Anal", "Ass", "Creampie" } }, { "BBCPie", new[] { "Interracial", "BBC", "Creampie" } },
            { "Cum4K", new[] { "Creampie" } }, { "DeepThroatLove", new[] { "Blowjob", "Deep Throat" } },
            { "GirlCum", new[] { "Orgasms", "Girl Orgasm", "Multiple Orgasms" } }, { "Holed", new[] { "Anal", "Ass" } },
            { "Lubed", new[] { "Lube", "Raw", "Wet" } }, { "MassageCreep", new[] { "Massage", "Oil" } },
            { "PassionHD", new[] { "Hardcore" } }, { "POVD", new[] { "Gonzo", "POV" } },
            { "PureMature", new[] { "MILF", "Mature" } },
        };
        private static readonly Dictionary<string, string[]> ActorsDB = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Poke Her In The Front", new[] { "Sara Luvv", "Dillion Harper" } },
            { "Best Friends With Nice Tits!", new[] { "April O'Neil", "Victoria Rae Black" } },
        };
        private static readonly Dictionary<string, string> Plurals = new Dictionary<string, string>
        {
            { "brothers", "brother-s" }, { "bros", "bro-s" }, { "sisters", "sister-s" }, { "siss", "sis-s" },
            { "mothers", "mother-s" }, { "moms", "mom-s" }, { "fathers", "father-s" }, { "dads", "dad-s" },
            { "sons", "son-s" }, { "daughters", "daughter-s" },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string directURL = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Slugify()}";
            if (char.IsDigit(directURL.Last()) && directURL[directURL.Length - 2] == '-')
            {
                directURL = $"{directURL.Substring(0, directURL.Length - 1)}-{directURL.Last()}";
            }

            var searchResultsURLs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { directURL };
            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                if (sceneURL.Contains("/video/"))
                {
                    searchResultsURLs.Add(sceneURL);
                }
            }

            var pluralResults = new HashSet<string>(searchResultsURLs, StringComparer.OrdinalIgnoreCase);
            foreach (var sceneURL in searchResultsURLs)
            {
                string pluralUrl = sceneURL;
                foreach (var plural in Plurals)
                {
                    pluralUrl = pluralUrl.Replace(plural.Key, plural.Value);
                }

                pluralResults.Add(pluralUrl);
            }

            foreach (var sceneURL in pluralResults.Select(url => url.Replace("www.", string.Empty)))
            {
                var req = await HTTP.Request(sceneURL, cancellationToken);
                if (!req.IsOK)
                {
                    continue;
                }

                var detailsPageElements = HTML.ElementFromString(req.Content);
                var titleNode = detailsPageElements.SelectSingleNode("//h1");
                if (titleNode == null)
                {
                    continue;
                }

                string titleNoFormatting = titleNode.InnerText.Trim();
                string curID = Helper.Encode(sceneURL);
                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } },
                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();

            if (sceneURL.Contains("pornplus") || sceneURL.Contains("tiny4k") || sceneURL.Contains("wetvr"))
            {
                movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(@class, 'space-x-4 items-start')]//span")?.InnerText.Trim();
            }
            else
            {
                movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(@id, 'description')]")?.InnerText.Trim();
            }

            movie.AddStudio("PornPros");

            var actorNodes = detailsPageElements.SelectNodes("//div[@id='t2019-sinfo']//a[contains(@href, '/girls/')] | //div[contains(@class, 'space-y-4 p-4')]//a[contains(@href, '/models/')]");
            string actorDate = null;
            string actorSubSite = null;

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

                foreach (var actorLink in actorNodes)
                {
                    foreach (var name in actorLink.InnerText.Trim().Split('&'))
                    {
                        result.People.Add(new PersonInfo { Name = name.Trim(), Type = PersonKind.Actor });
                    }

                    if (string.IsNullOrEmpty(actorDate))
                    {
                        string actorURL = actorLink.GetAttributeValue("href", string.Empty);
                        if (!actorURL.StartsWith("http"))
                        {
                            actorURL = Helper.GetSearchBaseURL(siteNum) + actorURL.Replace("girls?page=", string.Empty);
                        }

                        var actorPageElements = await HTML.ElementFromURL(actorURL, cancellationToken);
                        if(actorPageElements != null)
                        {
                            string sceneLinkXPath, sceneTitleXPath, sceneDateXPath, subSiteXPath, dateFormat;
                            if (sceneURL.Contains("pornplus") || sceneURL.Contains("wetvr"))
                            {
                                sceneLinkXPath = "//div[contains(@class, 'video-thumbnail flex')]";
                                sceneTitleXPath = ".//a[contains(@class, 'dropshadow')]";
                                sceneDateXPath = ".//span[contains(@class, 'font-extra-light')]/text()";
                                subSiteXPath = ".//a[contains(@class, 'series-link')]/img/@alt";
                                dateFormat = "MM/dd/yyyy";
                            }
                            else
                            {
                                sceneLinkXPath = "//div[@class='row']//div[contains(@class, 'box-shadow')]";
                                sceneTitleXPath = ".//h5[@class='card-title']";
                                sceneDateXPath = ".//@data-date";
                                subSiteXPath = string.Empty;
                                dateFormat = "MMMM dd, yyyy";
                            }

                            var sceneLinks = actorPageElements.SelectNodes(sceneLinkXPath);
                            if(sceneLinks != null)
                            {
                                foreach(var sceneLink in sceneLinks)
                                {
                                    string sceneTitle = Regex.Replace(sceneLink.SelectSingleNode(sceneTitleXPath)?.InnerText.Trim().Replace(" ", string.Empty) ?? string.Empty, @"\W", string.Empty).ToLower();
                                    if(Regex.Replace(movie.Name.Replace(" ", string.Empty), @"\W", string.Empty).ToLower() == sceneTitle)
                                    {
                                        if(!string.IsNullOrEmpty(subSiteXPath))
                                        {
                                            actorSubSite = sceneLink.SelectSingleNode(subSiteXPath)?.GetAttributeValue("alt", string.Empty).Trim();
                                        }

                                        actorDate = sceneLink.SelectSingleNode(sceneDateXPath)?.GetAttributeValue(sceneDateXPath.Split('@').Last(), string.Empty).Trim();
                                        if(!string.IsNullOrEmpty(actorDate))
                                        {
                                            if(DateTime.TryParseExact(actorDate, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                                            {
                                                movie.PremiereDate = parsedDate;
                                                movie.ProductionYear = parsedDate.Year;
                                            }
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            string siteName = (actorSubSite != null && sceneURL.Contains("pornplus")) ? actorSubSite : Helper.GetSearchSiteName(siteNum);
            movie.AddTag(siteName);

            if (ActorsDB.ContainsKey(movie.Name))
            {
                foreach(var actor in ActorsDB[movie.Name])
                {
                    result.People.Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
                }
            }

            if (!movie.PremiereDate.HasValue && !string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedSceneDate))
            {
                 movie.PremiereDate = parsedSceneDate;
                 movie.ProductionYear = parsedSceneDate.Year;
            }

            if(GenresDB.ContainsKey(siteName))
            {
                foreach(var genre in GenresDB[siteName])
                {
                    movie.AddGenre(genre);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var xpaths = new[] { "//dl8-video/@poster", "//div/video/@poster", "(//img[contains(@src, 'handtouched')])[position() < 5]/@src" };
            foreach(var xpath in xpaths)
            {
                var posterNodes = detailsPageElements.SelectNodes(xpath);
                if (posterNodes != null)
                {
                    foreach(var poster in posterNodes)
                    {
                        string posterUrl = poster.GetAttributeValue(xpath.Split('@').Last(), string.Empty);
                        if (!posterUrl.StartsWith("http"))
                        {
                            posterUrl = "http:" + posterUrl;
                        }

                        result.Add(new RemoteImageInfo { Url = posterUrl });
                    }
                }
            }

            // Set image types
            if(result.Any())
            {
                result.First().Type = ImageType.Primary;
                foreach(var image in result.Skip(1))
                {
                    image.Type = ImageType.Backdrop;
                }
            }

            return result;
        }
    }
}
