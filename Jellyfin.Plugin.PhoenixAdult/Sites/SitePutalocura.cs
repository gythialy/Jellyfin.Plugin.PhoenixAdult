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
    public class SitePutalocura : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new List<string>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken, "enes");
            foreach (var sceneURL in googleResults)
            {
                var url = sceneURL.Replace("index.php/", string.Empty).Replace("es/", string.Empty);
                if (!url.Contains("/tags/") && !url.Contains("/actr") && !url.Contains("?pag") && !url.Contains("/xvideos") && !url.Contains("/tag/") && !searchResults.Contains(url))
                {
                    searchResults.Add(url);
                    if (url.Contains("/en/"))
                    {
                        searchResults.Add(url.Replace("en/", string.Empty));
                    }
                }
            }

            foreach (var sceneURL in searchResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    var language = sceneURL.Contains("/en/") ? "English" : "Español";
                    var titleNoFormatting = doc.DocumentNode.SelectSingleNode("//title").InnerText.Split('|')[0].Split('-')[0].Trim();
                    var curID = Helper.Encode(sceneURL);
                    var date = doc.DocumentNode.SelectSingleNode("//div[@class='released-views']/span").InnerText.Trim();
                    if (DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        var releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{titleNoFormatting} {{{language}}} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//title").InnerText.Split('|')[0].Split('-')[0].Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[@class='description clearfix']").InnerText.Split(':').Last().Trim().Replace("\n", " ");
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//div[@class='categories']/a"))
            {
                movie.AddGenre(genreLink.InnerText.Trim());
            }

            var actors = new List<string>();
            var separator = sceneURL.Contains("/en/") ? " and " : " y ";
            if (movie.Name.Contains("&"))
            {
                actors.AddRange(movie.Name.Split('&').Select(a => a.Trim()));
            }
            else
            {
                actors.AddRange(doc.DocumentNode.SelectSingleNode("//span[@class='site-name']").InnerText.Split(new[] { separator }, StringSplitOptions.None).Select(a => a.Trim()));
            }

            foreach (var actorName in actors)
            {
                var correctedName = actorName.ToLower() == "africa" ? "Africat" : movie.Name == "MAMADA ARGENTINA" ? "Alejandra Argentina" : actorName == "Alika" ? "Alyka" : actorName;
                var modelURL = $"{Helper.GetSearchBaseURL(siteNum)}/actrices/{correctedName.First().ToString().ToLower()}";
                var modelHttp = await HTTP.Request(modelURL, cancellationToken);
                if (modelHttp.IsOK)
                {
                    var modelDoc = new HtmlDocument();
                    modelDoc.LoadHtml(modelHttp.Content);
                    var actorPhotoURL = modelDoc.DocumentNode.SelectSingleNode($"//div[@class='c-boxlist__box--image']//parent::a[contains(translate(., 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '{correctedName.ToLower()}')]//img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
                    result.AddPerson(new PersonInfo { Name = correctedName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var scriptText = doc.DocumentNode.SelectSingleNode("//div[@class='top-area-content']/script").InnerText;
                var posterImage = Regex.Match(scriptText, "(?<=posterImage: \").*(?=\")");
                if (posterImage.Success)
                {
                    images.Add(new RemoteImageInfo { Url = posterImage.Value, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
