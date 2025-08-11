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
using Jellyfin.Data.Enums;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteFamilyTherapyXXX : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle)) return result;

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(" ", "+")}";
            var searchPageElements = await HTML.ElementFromURL(searchUrl, cancellationToken);
            var searchResults = searchPageElements?.SelectNodes("//article");

            if (searchResults != null && searchResults.Any())
            {
                foreach (var searchResult in searchResults)
                {
                    var titleNode = searchResult.SelectSingleNode("./h2/a");
                    string titleNoFormatting = titleNode.InnerText.Trim();
                    string sceneURL = titleNode.GetAttributeValue("href", "");
                    string curID = Helper.Encode(sceneURL);
                    string date = searchResult.SelectSingleNode("./p/span[1]")?.InnerText.Trim();
                    string releaseDate = DateTime.Parse(date).ToString("yyyy-MM-dd");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|0" } },
                        Name = $"{titleNoFormatting} [FamilyTherapy] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
            }
            else
            {
                // Fallback to Clips4Sale
                var parts = searchTitle.Split(' ');
                if (parts.Length > 2)
                {
                    string title = string.Join(" ", parts.Skip(2));
                    string actress = string.Join(" ", parts.Take(2));
                    string c4sSearchTitle = $"81593 {actress}";

                    var c4sProvider = new SiteClips4Sale();
                    // Assuming siteNum for Clips4Sale is handled internally or needs to be looked up.
                    // For now, passing a placeholder. This might need adjustment.
                    var c4sResults = await c4sProvider.Search(new[] { 105 }, c4sSearchTitle, searchDate, cancellationToken);

                    foreach(var match in c4sResults)
                    {
                        match.Name = GetCleanTitle(match.Name);
                        string originalId = match.ProviderIds[Plugin.Instance.Name];
                        match.ProviderIds[Plugin.Instance.Name] = $"{originalId.Split('|')[0]}|{siteNum[0]}|1";
                        result.Add(match);
                    }
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            int mode = int.Parse(providerIds[2]);

            if (mode == 1) // Clips4Sale mode
            {
                var c4sProvider = new SiteClips4Sale();
                var c4sResult = await c4sProvider.Update(new[] { 105 }, new[] { sceneID[0] }, cancellationToken);
                result.Item = c4sResult.Item;
                result.People = c4sResult.People;
                result.Item.Name = GetCleanTitle(result.Item.Name);

                var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
                string actorName;
                try
                {
                    actorName = detailsPageElements.SelectSingleNode("//div[@class='[ mt-1-5 ] clip_details']/div[3]/span[2]/span[1]/a/text()")?.InnerText;
                }
                catch
                {
                    string summary = detailsPageElements.SelectSingleNode("//div[@class='individualClipDescription']/p/text()")?.InnerText;
                    actorName = new Regex(@"(?<=[Ss]tarring\s)\w*\s\w*").Match(summary).Value;
                }
                if(!string.IsNullOrEmpty(actorName))
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });

                result.Item.AddStudio("Family Therapy");
                return result;
            }

            // Direct scrape mode
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElementsDirect = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElementsDirect == null) return result;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElementsDirect.SelectSingleNode("//h1")?.InnerText.Trim();

            try { movie.Overview = detailsPageElementsDirect.SelectSingleNode("//div[@class='entry-content']/p[1]")?.InnerText.Trim(); }
            catch { movie.Overview = detailsPageElementsDirect.SelectSingleNode("//div[@class='entry-content']")?.InnerText.Trim(); }

            movie.AddStudio("Family Therapy");
            movie.AddTag("Family Therapy");

            var genreNodes = detailsPageElementsDirect.SelectNodes("//a[@rel='category tag']");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var dateNode = detailsPageElementsDirect.SelectSingleNode("//p[@class='post-meta']/span")?.InnerText.Trim();
            if (DateTime.TryParse(dateNode, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNode = detailsPageElementsDirect.SelectSingleNode("//div[@class='entry-content']/p[contains(text(),'starring') or contains(text(), 'Starring')]");
            if (actorNode != null)
            {
                string actorText = new Regex(@"(?<=[Ss]tarring\s)\w*\s\w*(\s&\s\w*\s\w*)*").Match(actorNode.InnerText).Value;
                foreach(var actorName in actorText.Split('&'))
                    result.People.Add(new PersonInfo { Name = actorName.Trim(), Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            string[] providerIds = sceneID[0].Split('|');
            int mode = int.Parse(providerIds[2]);
            if (mode == 1)
            {
                 var c4sProvider = new SiteClips4Sale();
                 return await c4sProvider.GetImages(new[] { 105 }, new[] { sceneID[0] }, item, cancellationToken);
            }

            // Direct scrape mode
            var result = new List<RemoteImageInfo>();
            // No images on direct page, would need to re-search
            return result;
        }

        private static string GetCleanTitle(string title)
        {
            return title.Replace(" (HD)", "").Replace(" (SD)", "").Trim();
        }
    }
}
