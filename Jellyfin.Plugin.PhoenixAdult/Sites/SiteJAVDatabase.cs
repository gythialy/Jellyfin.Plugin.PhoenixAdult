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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteJAVDatabase : IProviderBase
    {
        // This is a simplified version of the python provider.
        // The original script contains a massive amount of hardcoded data and complex logic which is not feasible to port.

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchJAVID = null;
            var splitSearchTitle = searchTitle.Split(' ');
            if (splitSearchTitle.Length > 1 && int.TryParse(splitSearchTitle[1], out _))
                searchJAVID = $"{splitSearchTitle[0]}-{splitSearchTitle[1]}";

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + (searchJAVID ?? Uri.EscapeDataString(searchTitle));
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'card h-100')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode(".//div[@class='mt-auto']/a")?.InnerText.Trim();
                    string javid = node.SelectSingleNode(".//p//a[contains(@class, 'cut-text')]")?.InnerText.Trim();
                    string sceneUrl = node.SelectSingleNode(".//p//a[contains(@class, 'cut-text')]")?.GetAttributeValue("href", "").Trim();
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//div[@class='mt-auto']/text()[2]");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"[{javid}] {releaseDate} {titleNoFormatting}",
                        SearchProviderName = Plugin.Instance.Name
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            string javId = detailsPageElements.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "").Trim();
            string title = detailsPageElements.SelectSingleNode("//*[text()='Title: ']/following-sibling::text()")?.InnerText.Trim();
            movie.Name = $"[{javId.ToUpper()}] {title}";

            var studioNode = detailsPageElements.SelectSingleNode("//*[text()='Studio: ']/following-sibling::span/a");
            if(studioNode != null)
                movie.AddStudio(studioNode.InnerText.Trim());

            if(movie.Studio != null)
                movie.AddCollection(new[] { movie.Studio });
            else
                movie.AddCollection(new[] { "Japan Adult Video" });

            var dateNode = detailsPageElements.SelectSingleNode("//*[text()='Release Date: ']/following-sibling::text()[1]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//*[text()='Genre(s): ']/following-sibling::*/a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//*[text()='Idol(s)/Actress(es): ']/following-sibling::span/a");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
            }

            var directorNode = detailsPageElements.SelectSingleNode("//*[text()='Director: ']/following-sibling::span/a");
            if(directorNode != null)
                result.People.Add(new PersonInfo { Name = directorNode.InnerText.Trim(), Type = PersonKind.Director });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var imageNodes = detailsPageElements.SelectNodes("//tr[@class='moviecovertb']//img | //div/div[./h2[contains(., 'Images')]]/a");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", "") ?? img.GetAttributeValue("href", "") });
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
