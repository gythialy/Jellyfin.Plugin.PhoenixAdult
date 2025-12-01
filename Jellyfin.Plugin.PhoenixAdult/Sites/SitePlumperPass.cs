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
    public class SitePlumperPass : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new List<string>();
            var sceneID = searchTitle.Split(' ').First();

            if (int.TryParse(sceneID, out _))
            {
                var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/t1/refstat.php?lid={sceneID}&sid=584";
                searchResults.Add(sceneURL);
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var r in googleResults)
            {
                var match = Regex.Match(r, @"((?<=\dpp\/)|(?<=\dbbwd\/)|(?<=\dhsp\/)|(?<=\dbbbj\/)|(?<=\dpatp\/)|(?<=\dftf\/)|(?<=\dbgb\/))\d+(?=\/)");
                if (match.Success)
                {
                    var id = match.Groups[0].Value;
                    var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/t1/refstat.php?lid={id}&sid=584";
                    if (r.Contains("content") && !searchResults.Contains(sceneURL))
                    {
                        searchResults.Add(sceneURL);
                    }
                }
            }

            foreach (var sceneURL in searchResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK && http.Content.Contains("content"))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);

                    var titleNoFormatting = doc.DocumentNode.SelectSingleNode("//h2[@class='vidtitle']").InnerText.Trim().Replace("\"", string.Empty);
                    var curID = Helper.Encode(sceneURL);
                    var releaseDate = string.Empty;
                    var dateNode = doc.DocumentNode.SelectNodes("//h3[@class='releases']//br/preceding-sibling::text()");
                    if (dateNode != null && DateTime.TryParseExact(dateNode.Last().InnerText.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
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
            movie.ExternalId = sceneURL;
            movie.Name = Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//h2[@class='vidtitle']").InnerText.Trim().Replace("\"", string.Empty), siteNum);
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'vidinfo')]/p").InnerText.Trim();
            movie.AddStudio("PlumperPass");

            var url = http.ResponseUrl.ToString();
            if (url.Contains("bbwd/"))
            {
                movie.AddCollection("BBW Dreams");
            }
            else if (url.Contains("bbbj/"))
            {
                movie.AddCollection("Big Babe Blowjobs");
            }
            else if (url.Contains("hsp/"))
            {
                movie.AddCollection("Hot Sexy Plumpers");
            }
            else if (url.Contains("patp/"))
            {
                movie.AddCollection("Plumpers At Play");
            }
            else if (url.Contains("ftf/"))
            {
                movie.AddCollection("First Time Fatties");
            }
            else if (url.Contains("bgb/"))
            {
                movie.AddCollection("BBWs Gone Black");
            }
            else
            {
                movie.AddCollection("PlumperPass");
            }

            var dateNode = doc.DocumentNode.SelectNodes("//h3[@class='releases']//br/preceding-sibling::text()");
            if (dateNode != null && DateTime.TryParseExact(dateNode.Last().InnerText.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genres = doc.DocumentNode.SelectNodes("//p[@class='tags clearfix']/a") ?? doc.DocumentNode.SelectNodes("//meta[@name='keywords']");
            if (genres != null)
            {
                var genreList = genres.Select(g => g.Name == "meta" ? g.GetAttributeValue("content", string.Empty).Split(',') : new[] { g.InnerText }).SelectMany(g => g);
                foreach (var genreName in genreList)
                {
                    movie.AddGenre(genreName.Trim());
                }
            }

            var actors = doc.DocumentNode.SelectNodes("//h3[@class='releases']/a");
            if (actors != null)
            {
                if (actors.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actors.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actors.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actorLink in actors)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorPageURL = $"{Helper.GetSearchBaseURL(siteNum)}/t1/{actorLink.GetAttributeValue("href", string.Empty).Trim()}";
                    var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorDoc = new HtmlDocument();
                        actorDoc.LoadHtml(actorHttp.Content);
                        var actorPhotoURL = $"{Helper.GetSearchBaseURL(siteNum)}/t1/{actorDoc.DocumentNode.SelectSingleNode("//div[@class='row mainrow']//img").GetAttributeValue("src", string.Empty).Trim()}";
                        result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                    }
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

                var videoImage = doc.DocumentNode.SelectSingleNode("//div[@class='movie-big']//script").InnerText;
                var pattern = new Regex("(?<=image: \").*(?=\")");
                if (pattern.IsMatch(videoImage))
                {
                    var imageID = pattern.Match(videoImage).Value;
                    var img = $"{Helper.GetSearchBaseURL(siteNum)}/t1/{imageID}";
                    images.Add(new RemoteImageInfo { Url = img, Type = ImageType.Primary });
                }

                var trailerImage = doc.DocumentNode.SelectSingleNode("//div[@class='movie-trailer']//img");
                if (trailerImage != null)
                {
                    var img = trailerImage.GetAttributeValue("src", string.Empty);
                    if (!img.StartsWith("http"))
                    {
                        img = $"{Helper.GetSearchBaseURL(siteNum)}/t1/{img}";
                    }

                    images.Add(new RemoteImageInfo { Url = img, Type = ImageType.Backdrop });
                }
            }

            return images;
        }
    }
}
