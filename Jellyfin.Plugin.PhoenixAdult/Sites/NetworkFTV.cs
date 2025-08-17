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
    public class NetworkFTV : IProviderBase
    {
        private static readonly Dictionary<int, List<string>> photoScenes = new Dictionary<int, List<string>>
        {
            {226, new List<string> {"cool-colors", "shes-on-fire", "heating-up"}},
            {209, new List<string> {"amazing-figure"}},
            {210, new List<string> {"supersexy-vixen", "satin-sensuality", "outdoor-finale"}},
            {130, new List<string> {"elegantly-sexual"}},
            {1569, new List<string> {"model-like-no-other", "teen-penetration"}},
            {1524, new List<string> {"petite-gaping", "penetration-limits"}},
            {1573, new List<string>()},
            {283, new List<string>()},
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? "", out _))
            {
                sceneId = searchTitle.Split(' ').First();
                searchTitle = searchTitle.Replace(sceneId, "").Trim();
            }

            var searchResults = new List<string>();
            if (sceneId != null)
                searchResults.Add($"{Helper.GetSearchSearchURL(siteNum)}{sceneId}.html");

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(u => u.Contains("/update/")));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    var titleDate = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split(new[] { "Released" }, StringSplitOptions.None);
                    if (titleDate != null && titleDate.Length > 1)
                    {
                        string titleNoFormatting = titleDate[0].Trim();
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = string.Empty;
                        if (DateTime.TryParse(titleDate.Last().Replace("!", "").Trim(), out var parsedDate))
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name
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
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            var titleDate = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split(new[] { "Released" }, StringSplitOptions.None);
            movie.Name = titleDate[0].Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@id='Bio']")?.InnerText.Trim();
            movie.AddStudio("First Time Videos");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (DateTime.TryParse(titleDate.Last().Replace("!", "").Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genres = new List<string>();
            if (tagline == "FTVGirls")
                genres.AddRange(new[] { "Teen", "Solo", "Public" });
            else if (tagline == "FTVMilfs")
                genres.AddRange(new[] { "MILF", "Solo", "Public" });
            foreach(var genre in genres)
                movie.AddGenre(genre);

            var actorNodes = detailsPageElements.SelectNodes("//div[@id='ModelDescription']//h1");
            if(actorNodes != null)
            {
                for(int i=0; i < actorNodes.Count; i++)
                {
                    string actorName = actorNodes[i].InnerText.Replace("'s Statistics", "").Trim();
                    string actorPhotoUrl = detailsPageElements.SelectSingleNode($"//div[@id='Thumbs']/img[{i+1}]")?.GetAttributeValue("src", "");
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var match = Regex.Match(sceneUrl, @"-([0-9]{1,})\.");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
            {
                var scenes = photoScenes.ContainsKey(id) ? photoScenes[id] : new List<string> { "none" };
                foreach (var photoUrl in scenes)
                {
                    if (photoUrl.Contains("galleries") || photoUrl.Contains("preview"))
                    {
                        var httpResult = await HTTP.Request(photoUrl, HttpMethod.Get, cancellationToken);
                        if (httpResult.IsOK)
                        {
                            var photoPageElements = HTML.ElementFromString(httpResult.Content);
                            var imageNodes = photoPageElements.SelectNodes("//img[@id='Magazine'] | //div[@class='gallery']//div[@class='row']//a | //div[@class='thumbs_horizontal']//a | //a[img[@class='t']]");
                            if (imageNodes != null)
                            {
                                foreach(var img in imageNodes)
                                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", "") ?? img.GetAttributeValue("href", "") });
                            }
                        }
                    }
                }
            }
            return images;
        }
    }
}
