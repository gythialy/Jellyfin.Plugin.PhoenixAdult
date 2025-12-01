using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class SiteMomComesFirst : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchClean = searchTitle.Replace("sons", string.Empty).Replace("mothers", string.Empty).Replace("moms", string.Empty).Replace("'", string.Empty).ToLower();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + string.Join("+", searchClean.Split(' '));
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//article"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode("./h2").InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode("./h2/a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);
                    var date = searchResult.SelectSingleNode("./p/span").InnerText.Trim();
                    var releaseDate = string.Empty;
                    if (!string.IsNullOrEmpty(date) && DateTime.TryParseExact(date, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//h1").InnerText.Trim(), siteNum);

            var summaryParts = new List<string>();
            foreach (var part in doc.DocumentNode.SelectNodes("//div[@class='entry-content']/p"))
            {
                if (!part.InnerText.ToLower().Contains("starring"))
                {
                    summaryParts.Add(part.InnerText.Trim());
                }
            }

            movie.Overview = string.Join("\n", summaryParts);
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='published']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actors = new List<string>();
            foreach (var genreLink in doc.DocumentNode.SelectNodes("//a[contains(@rel, 'tag')]"))
            {
                var genreName = Helper.ParseTitle(genreLink.InnerText.Trim(), siteNum);
                if (ActorsDB.Contains(genreName.ToLower()))
                {
                    actors.Add(genreName);
                }
                else
                {
                    movie.AddGenre(genreName);
                }
            }

            var actorSubtitle = doc.DocumentNode.SelectNodes("//div[@class='entry-content']/p").LastOrDefault();
            if (actorSubtitle != null && actorSubtitle.InnerText.ToLower().Contains("starring"))
            {
                actors.AddRange(actorSubtitle.InnerText.Split(new[] { "Starring" }, StringSplitOptions.None).Last().Split('*')[0].Split('&').Select(a => a.Trim()));
            }

            foreach (var actorName in actors.Distinct())
            {
                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(new List<RemoteImageInfo>());
        }

        private static readonly HashSet<string> ActorsDB = new HashSet<string>
        {
            "alex adams", "brianna beach", "sophia locke", "crystal rush", "tucker stevens", "coralia baby", "juliett russo",
            "demi diveena", "mandy waters", "mandy rhea", "april love", "kate dee", "emma magnolia", "naomi foxxx", "rachael cavalli",
            "cory chase", "abby somers", "bailey base", "kiki klout", "victoria june", "kendra heart", "archie stone", "jaime vine",
            "casca akashova", "katie monroe", "eve rebel", "dakota burns", "nikita reznikova", "taylor blake", "tricia oaks", "artemisia love",
            "kyla keys", "brianna rose", "jordan max", "jadan snow", "kaylynn keys", "lucy sunflower", "jackie hoff", "kenzie foxx",
            "mirabella amore", "heather vahn", "natasha nice", "kat marie", "miss brat", "macy meadows", "sydney paige", "vanessa cage",
        };
    }
}
