using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using System.Net.Http;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteMyDirtyHobby : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
            };
            var result = new List<RemoteSearchResult>();
            var searchUrl = Helper.GetSearchSearchURL(siteNum);
            var searchBody = JsonConvert.SerializeObject(new { country = "us", keyword = searchTitle, user_language = "en" });

            var http = await HTTP.Request(searchUrl, HttpMethod.Post, new StringContent(searchBody), headers, null, cancellationToken);
            if (http.IsOK)
            {
                var json = JObject.Parse(http.Content);
                var items = json["items"];
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if ((string)item["contentType"] != "video")
                        {
                            continue;
                        }

                        var titleNoFormatting = (string)item["title"];
                        var userId = (string)item["u_id"];
                        var userVideoId = (string)item["uv_id"];
                        var userNickname = (string)item["nick"];
                        var cleanTitle = Regex.Replace(titleNoFormatting, @"[^-A-z0-9]+", "-").ToLower();
                        var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/profil/{userId}-{userNickname}/videos/{userVideoId}-{cleanTitle}";
                        var curID = Helper.Encode(sceneURL);

                        var date = (string)item["onlineAt"];
                        var releaseDate = string.Empty;
                        if (!string.IsNullOrEmpty(date))
                        {
                            if (DateTime.TryParseExact(date, "dd/MM/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{Helper.ParseTitle(titleNoFormatting, siteNum)} [{Helper.GetSearchSiteName(siteNum)}/{userNickname}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = (string)item["thumb_278_156"],
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
            var movie = (Movie)result.Item;

            var json = await GetPageJson(Helper.Decode(sceneID[0]), cancellationToken);
            if (json == null)
            {
                return result;
            }

            var videoPageElements = json.SelectToken("content");
            var userPageElements = json.SelectToken("profileHeader.profileAvatar");

            movie.Name = Helper.ParseTitle((string)videoPageElements.SelectToken("title.text"), siteNum).Trim();
            movie.Overview = (string)videoPageElements.SelectToken("description.text");
            movie.AddStudio("My Dirty Hobby");

            var tagline = (string)userPageElements.SelectToken("title");
            if (!string.IsNullOrEmpty(tagline))
            {
                movie.AddCollection(tagline.Trim());
            }

            var date = (string)videoPageElements.SelectToken("subtitle.text");
            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var categories = videoPageElements.SelectToken("categories.items");
            if (categories != null)
            {
                foreach (var category in categories)
                {
                    movie.AddGenre(((string)category["text"]).Trim().ToLower());
                }
            }

            var actorName = (string)userPageElements.SelectToken("title");
            var actorPhotoURL = (string)userPageElements.SelectToken("thumbImg.src");
            result.AddPerson(new PersonInfo { Name = actorName.Trim(), Type = PersonKind.Actor, ImageUrl = actorPhotoURL });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var json = await GetPageJson(Helper.Decode(sceneID[0]), cancellationToken);
            if (json != null)
            {
                var posterUrl = (string)json.SelectToken("content.videoNotPurchased.thumbnail.src");
                if (!string.IsNullOrEmpty(posterUrl))
                {
                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }

        private async Task<JObject> GetPageJson(string sceneURL, CancellationToken cancellationToken)
        {
            var http = await HTTP.Request(sceneURL, cancellationToken, null, new Dictionary<string, string> { { "AGEGATEPASSED", "1" } });
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var scriptNode = doc.DocumentNode.SelectSingleNode("//div[./div[@id='profile_page']]/script");
                if (scriptNode != null)
                {
                    var match = Regex.Match(scriptNode.InnerText, @"\{.*\};", RegexOptions.Singleline);
                    if (match.Success)
                    {
                        var jsonString = match.Value.TrimEnd(';');
                        return JObject.Parse(jsonString);
                    }
                }
            }

            return null;
        }
    }
}
