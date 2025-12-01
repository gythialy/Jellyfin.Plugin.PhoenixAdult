using System;
using System.Collections.Generic;
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
    public class SitePuba : IProviderBase
    {
        private readonly Dictionary<string, string> _headers = new Dictionary<string, string> { { "Referer", "https://www.puba.com/pornstarnetwork/index.php" } };
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "PHPSESSID", "rvo9ieo5bhoh81knnmu88c3lf3" } };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new List<string>();
            var sceneID = searchTitle.Split(' ').First();
            if (int.TryParse(sceneID, out _))
            {
                var directURL = $"{Helper.GetSearchSearchURL(siteNum)}show_video.php?galid={sceneID}";
                searchResults.Add(directURL);
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(url => url.Contains("show_video") && !url.Contains("index") && !searchResults.Contains(url)));

            foreach (var sceneURL in searchResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken, _headers, _cookies);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    var titleNoFormatting = doc.DocumentNode.SelectSingleNode("//div[@id='body-player-container']//div//div[@class='tour-video-title']").InnerText.Trim();
                    var curID = Helper.Encode(sceneURL);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"[{Helper.GetSearchSiteName(siteNum)}] {titleNoFormatting}",
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken, _headers, _cookies);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@id='body-player-container']//div//div[@class='tour-video-title']").InnerText.Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            foreach (var genre in doc.DocumentNode.SelectNodes("//center//div//a[contains(@class, 'btn-outline-secondary')]"))
            {
                var genreName = genre.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            foreach (var actor in doc.DocumentNode.SelectNodes("//center//div//a[contains(@class, 'btn-secondary')]"))
            {
                var actorName = actor.InnerText.Trim();
                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken, _headers, _cookies);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var style = doc.DocumentNode.SelectSingleNode("//div[@id='body-player-container']/div/a/img").GetAttributeValue("style", string.Empty);
                var match = Regex.Match(style, @"url\((.*?)\)");
                if (match.Success)
                {
                    var posterUrl = Helper.GetSearchBaseURL(siteNum) + match.Groups[1].Value;
                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
