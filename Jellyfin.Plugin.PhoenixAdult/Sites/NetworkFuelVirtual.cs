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
    public class NetworkFuelVirtual : IProviderBase
    {
        private static readonly Dictionary<string, Dictionary<int, List<string>>> actor_db = new Dictionary<string, Dictionary<int, List<string>>>
        {
            { "FuckedHard18", new Dictionary<int, List<string>> {
                {434, new List<string> {"Abby Lane"}}, {435, new List<string> {"Abby Cross"}}, {445, new List<string> {"Alexa Rydell"}},
                {446, new List<string> {"Alexa Nicole"}}, {469, new List<string> {"Alexis Grace"}}, {470, new List<string> {"Ashley Abott"}},
                {474, new List<string> {"Ashlyn Molloy"}}, {481, new List<string> {"Ava White"}}, {482, new List<string> {"Ava Sparxxx"}},
                {483, new List<string> {"Ava Taylor"}}, {486, new List<string> {"Dahlia Sky"}}, {487, new List<string> {"Bailey Bam"}},
                {501, new List<string> {"Callie Cobra"}}, {502, new List<string> {"Callie Cyprus"}}, {503, new List<string> {"Callie Calypso"}},
            }                        },
            { "MassageGirls18", new Dictionary<int, List<string>> {
                {134, new List<string> {"Melissa Mathews"}}, {135, new List<string> {"Melissa Mathews"}}, {137, new List<string> {"Abby Paradise"}},
                {138, new List<string> {"Abby Cross"}}, {139, new List<string> {"Abby Lane"}}, {147, new List<string> {"Alexa Nicole"}},
            }                        },
            { "NewGirlPOV", new Dictionary<int, List<string>> {
                {1159, new List<string> {"Ashley Adams"}}, {1178, new List<string> {"Lola Hunter"}}, {1206, new List<string> {"Molly Manson"}},
                {1242, new List<string> {"Naomi Woods"}}, {1280, new List<string> {"Melissa Moore"}},
            }                        },
        };

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
            var searchNodes = searchPageElements.SelectNodes("//div[@align='left']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//td[@valign='top'][2]/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//span[@class='date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Added", string.Empty).Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [FuelVirtual/{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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
            string siteName = Helper.GetSearchSiteName(siteNum).Trim();
            string sceneUrl = Helper.Decode(providerIds[0]);
            string serverPath = siteName == "NewGirlPOV" ? "/tour/newgirlpov/" : "/membersarea/";
            sceneUrl = Helper.GetSearchBaseURL(siteNum) + serverPath + sceneUrl;
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            if (siteName == "NewGirlPOV")
            {
                movie.Name = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split(' ')[1].Trim();
            }
            else
            {
                movie.Name = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split('-')[0].Trim();
            }

            movie.AddStudio("FuelVirtual");
            movie.AddTag(siteName);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//td[@class='plaintext']/a[@class='model_category_link']");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }
            if (siteName != "NewGirlPOV")
            {
                movie.AddGenre("18-Year-Old");
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@id='description']//td[@align='left']/a");
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

                var match = Regex.Match(sceneUrl, @"id=(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var id) && actor_db.ContainsKey(siteName) && actor_db[siteName].ContainsKey(id))
                {
                    foreach (var actorName in actor_db[siteName][id])
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
                else
                {
                    foreach (var actor in actorNodes)
                    {
                        result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string siteName = Helper.GetSearchSiteName(siteNum).Trim();
            string sceneUrl = Helper.Decode(providerIds[0]);
            string serverPath = siteName == "NewGirlPOV" ? "/tour/newgirlpov/" : "/membersarea/";
            sceneUrl = Helper.GetSearchBaseURL(siteNum) + serverPath + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//a[@class='jqModal']/img | //div[@id='overallthumb']/a/img");
            if (imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty);
                    if (imageUrl.StartsWith("/"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }
                    else
                    {
                        imageUrl = $"{Helper.GetSearchBaseURL(siteNum)}/tour/newgirlpov/{imageUrl}";
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl });
                }
            }

            string photoPageUrl = sceneUrl.Replace("vids", "highres");
            var photoHttp = await HTTP.Request(photoPageUrl, HttpMethod.Get, cancellationToken);
            if(photoHttp.IsOK)
            {
                var photoPage = HTML.ElementFromString(photoHttp.Content);
                var photoNodes = photoPage.SelectNodes("//a[@class='jqModal']/img");
                if(photoNodes != null)
                {
                    foreach(var img in photoNodes)
                    {
                        images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + img.GetAttributeValue("src", string.Empty) });
                    }
                }
            }

            var match = Regex.Match(httpResult.Content, @"image:\s*""(.+)""");
            if (match.Success)
            {
                images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + match.Groups[1].Value });
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
