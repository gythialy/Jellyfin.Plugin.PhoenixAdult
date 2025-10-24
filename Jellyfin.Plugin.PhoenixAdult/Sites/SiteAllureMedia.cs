using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteAllureMedia : IProviderBase
    {
        private static readonly List<string> actors = new List<string>
        {
            "Adriana Chechik", "Alexa Grace", "Alice Green", "Alina Li", "Amanda Aimes", "Ava Taylor", "Belle Knox",
            "Belle Noire", "Cadence Lux", "Claire Heart", "Devon Green", "Emily Grey", "Emma Hix", "Evangeline", "Faith",
            "Hadley", "Holly Michaels", "Jane Wilde", "Kennedy Leigh", "Kenzie Reeves", "Kimber Woods", "Layla London",
            "Lexi Mansfield", "Lilly Banks", "Linda Lay", "Lucy Tyler", "Melissa Moore", "Nadine Sage", "Naomi Woods",
            "Remy LaCroix", "Samantha Sin", "Stella May", "Talia Tyler", "Taylor Renae", "Veronica Rodriguez", "Zoe Voss",
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
            var searchNodes = searchPageElements.SelectNodes("//div[@class='update_details']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = string.Empty;
                    string releaseDate = string.Empty;
                    string curId = string.Empty;

                    if (siteNum[0] == 564) // Amateur Allure
                    {
                        titleNoFormatting = node.SelectSingleNode(".//div[@class='update_title']/a")?.InnerText.Trim();
                        var dateNode = node.SelectSingleNode(".//div[@class='update_date']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Added:", string.Empty).Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        curId = Helper.Encode(node.SelectSingleNode(".//a[1]")?.GetAttributeValue("href", string.Empty));
                    }
                    else if (siteNum[0] == 565) // Swallow Salon
                    {
                        titleNoFormatting = node.SelectSingleNode("./a[2]")?.InnerText.Trim();
                        var dateNode = node.SelectSingleNode(".//div[@class='cell update_date']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        curId = Helper.Encode(node.SelectSingleNode("./a[2]")?.GetAttributeValue("href", string.Empty));
                    }

                    if (!string.IsNullOrEmpty(titleNoFormatting))
                    {
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//title")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//span[@class='update_description']")?.InnerText.Trim();
            movie.AddStudio("Allure Media");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'update_date')]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//span[@class='update_tags']//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
                }
            }

            movie.AddGenre("Amateur");

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='backgroundcolor_info']//span[@class='update_models']/a");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    string actorUrl = actorNode.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='cell_top cell_thumb']/img")?.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                        {
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                        }
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            foreach (var actorName in actors)
            {
                if ((movie.Name != null && movie.Name.Contains(actorName, StringComparison.OrdinalIgnoreCase)) || (movie.Overview != null && movie.Overview.Contains(actorName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!result.People.Any(p => p.Name.Equals(actorName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var scriptNode = detailsPageElements.SelectSingleNode("//script[contains(text(), 'df_movie')]");
            if (scriptNode != null)
            {
                var match = Regex.Match(scriptNode.InnerText, "useimage = \"([^\"]+)\"");
                if (match.Success)
                {
                    string imageUrl = match.Groups[1].Value;
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
