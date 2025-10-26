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
    public class NetworkAdultPrime : IProviderBase
    {
        private static readonly HashSet<string> skipGeneric = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "One-stop shop for all your dirty needs",
            "CFNM stands for clothed-female-nude-male",
            "Coming straight from Germany, on BBVideo",
            "BeautyAndTheSenior is all about giving some",
            "Perfect18 brings you fresh new girls",
            "If you are somebody who truly appreciates the beauty of the bondage",
            "Submissed has a rather straightforward name",
            "Latina girls are known to be some of the most beautiful",
            "It's the BreedBus, come on hop and join us",
            "ClubBangBoys is one of the hottest gay porn",
            "With such a straightforward name, one can already guess what ClubCastings",
            "Welcome to our dirty filthy porn hospital at DirtyHospital.com",
            "Distorded.com brings you a big fetish variety",
            "Coming to you exclusively from Nathan Blake Production, ElegantRaw",
            "Evil Playgrounds is a great brand if you like tight young Eastern European",
            "FamilyScrew is all about keeping it in the family",
            "We all have different preferences when it comes to porn, which is why FetishPrime",
            "Fixxxion is an adventurous fantasy",
            "Welcome to FreshPOV.com videos",
            "FuckingSkinny gives you exactly that",
            "Gonzo2000.com is bringing you a selection",
            "The older the babes, the more experience they have",
            "If you want to watch experienced older couples",
            "GroupBanged is filled with the most cum-thirsty",
            "GroupMams.com is bringing you exactly what it says",
            "When a group of horny people gets together",
            "When you remember that Amsterdam is the sex capital of the world",
            "From couples having some passionate fun to hardcore threesomes",
            "Jim Slip follows the life of the luckiest man on Earth",
            "All the videos featured on YoungBusty",
            "You can meet actual adult film stars on the streets of Prague",
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            if (sceneId != null)
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/studios/video/{sceneId}";
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1")?.InnerText.Split(':').Last().Trim(), siteNum);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//p[@class='update-info-line regular']/b[1][./preceding-sibling::i[contains(@class, 'calendar')]]");
                    if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
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
                        Name = $"{titleNoFormatting} [Adult Prime] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                string encodedTitle = Uri.EscapeDataString(searchTitle);
                for (int i = 0; i < 2; i++)
                {
                    string searchUrl = (i == 0)
                        ? $"{Helper.GetSearchSearchURL(siteNum)}video&q={encodedTitle}"
                        : $"{Helper.GetSearchSearchURL(siteNum)}performer&q={encodedTitle}";

                    var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var searchPageElements = HTML.ElementFromString(httpResult.Content);
                        var searchNodes = searchPageElements.SelectNodes("//ul[@id='studio-videos-container']/li");
                        if (searchNodes != null)
                        {
                            foreach (var node in searchNodes)
                            {
                                string titleNoFormatting = node.SelectSingleNode(".//span[contains(@class, 'title')]")?.InnerText.Trim();
                                string galleryId = node.SelectSingleNode(".//div[contains(@class, 'overlay inline-preview')]")?.GetAttributeValue("data-id", string.Empty);
                                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/studios/video/{galleryId}";
                                string curId = Helper.Encode(sceneUrl);

                                string releaseDate = string.Empty;
                                var dateNode = node.SelectSingleNode(".//span[contains(@class, 'releasedate')]");
                                if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                                {
                                    releaseDate = parsedDate.ToString("yyyy-MM-dd");
                                }
                                else if (searchDate.HasValue)
                                {
                                    releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                                }

                                if (result.All(r => r.ProviderIds[Plugin.Instance.Name] != $"{curId}|{siteNum[0]}|{releaseDate}"))
                                {
                                    result.Add(new RemoteSearchResult
                                    {
                                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                        Name = $"{titleNoFormatting} [Adult Prime] {releaseDate}",
                                        SearchProviderName = Plugin.Instance.Name,
                                    });
                                }
                            }
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
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1")?.InnerText.Split(':').Last().Split(new[] { "Full video by" }, StringSplitOptions.None)[0].Trim(), siteNum);

            string summary = detailsPageElements.SelectSingleNode("//p[contains(@class, 'description')]")?.InnerText.Trim();
            if (summary != null && !skipGeneric.Any(s => summary.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                movie.Overview = summary;
            }

            movie.AddStudio(detailsPageElements.SelectSingleNode("//p[@class='update-info-line regular'][./b[contains(., 'Studio')]]//a")?.InnerText.Trim());

            string tagline = detailsPageElements.SelectSingleNode("//p[@class='update-info-line regular'][./b[contains(., 'Series')]]//a[2]")?.InnerText.Trim();
            if (tagline != null)
            {
                movie.AddTag(tagline);
            }

            var dateNode = detailsPageElements.SelectSingleNode("//p[@class='update-info-line regular']/b[1][./preceding-sibling::i[contains(@class, 'calendar')]]");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectSingleNode("//p[@class='update-info-line regular'][./b[contains(., 'Niches')]]")?.InnerText.Split(':').Last().Split(',');
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(Helper.ParseTitle(genre.Trim(), siteNum));
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//p[@class='update-info-line regular'][./b[contains(., 'Performer')]]/a");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    string actorPhotoUrl = string.Empty;

                    string modelPageUrl = $"{Helper.GetSearchSearchURL(siteNum)}performer&q={Uri.EscapeDataString(actorName)}";
                    var modelHttpResult = await HTTP.Request(modelPageUrl, HttpMethod.Get, cancellationToken);
                    if (modelHttpResult.IsOK)
                    {
                        var modelPageElements = HTML.ElementFromString(modelHttpResult.Content);
                        var actorPhotoNode = modelPageElements.SelectSingleNode("//div[@class='performer-container']//div[@class='ratio-square']");
                        if (actorPhotoNode != null)
                        {
                            var style = actorPhotoNode.GetAttributeValue("style", string.Empty);
                            actorPhotoUrl = style.Split('(').Last().Split(')').First().Replace("'", "");
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//video[@id]/@poster");
            if (posterNode != null)
            {
                string imageUrl = posterNode.GetAttributeValue("poster", string.Empty);
                if (!imageUrl.StartsWith("http"))
                {
                    imageUrl = imageUrl.Split('(').Last().Split(')').First().Replace("'", "");
                }

                images.Add(new RemoteImageInfo { Url = imageUrl.Split('?')[0], Type = ImageType.Primary });
            }

            return images;
        }
    }
}
