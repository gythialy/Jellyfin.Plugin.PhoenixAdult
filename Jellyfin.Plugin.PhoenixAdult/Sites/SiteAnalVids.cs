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
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteAnalVids : IProviderBase
    {
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
                try
                {
                    string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/watch/{sceneId}";
                    string curId = Helper.Encode(sceneUrl);
                    var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var scenePageElements = HTML.ElementFromString(httpResult.Content);
                        string titleNoFormatting = scenePageElements.SelectSingleNode("//h1[contains(@class,'watch__title')]")?.InnerText.Trim();
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [AnalVids]",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
                catch { }
            }

            var searchHttp = await HTTP.Request(Helper.GetSearchSearchURL(siteNum) + searchTitle, HttpMethod.Get, cancellationToken);
            if (searchHttp.IsOK)
            {
                var searchResults = JObject.Parse(searchHttp.Content);
                var terms = searchResults["terms"];
                if (terms != null)
                {
                    foreach (var term in terms)
                    {
                        if (term["type"].ToString() == "scene")
                        {
                            string titleNoFormatting = term["name"].ToString();
                            string sceneUrl = term["url"].ToString();
                            string curId = Helper.Encode(sceneUrl);
                            string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                Name = $"{titleNoFormatting} [AnalVids] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
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
            movie.Name = detailsPageElements.SelectSingleNode("//h1[contains(@class,'watch__title')]")?.InnerText.Trim().Split(new[] { "featuring" }, StringSplitOptions.None)[0];
            movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(@class,'text-mob-more')]")?.InnerText;
            movie.AddStudio("AnalVids");

            string tagline = detailsPageElements.SelectSingleNode("//div[contains(@class,'genres-list')]/a")?.InnerText.Trim();
            if (tagline != null)
            {
                movie.AddTag(tagline);
                }

            var dateNode = detailsPageElements.SelectSingleNode("//i[contains(@class,'bi-calendar3')]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'genres-list')]/a[contains(@href, '/genre/')]");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[contains(@href, '/model/') and not(contains(@href, 'forum'))]");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[contains(@class,'model')]//img")?.GetAttributeValue("src", string.Empty);
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            if (tagline == "Giorgio Grandi" || tagline == "Giorgio's Lab")
            {
                result.People.Add(new PersonInfo { Name = "Giorgio Grandi", Type = PersonKind.Director });
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

            var posterNode = detailsPageElements.SelectSingleNode("//div[contains(@class,'watch__video')]//video");
            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("data-poster", string.Empty), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
