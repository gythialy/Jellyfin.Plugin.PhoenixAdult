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
    public class SiteData18Scenes : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new HashSet<string>();
            var siteResults = new HashSet<string>();
            var temp = new List<RemoteSearchResult>();

            string sceneID = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var parsedId) && parsedId > 100)
            {
                sceneID = parts[0];
                searchTitle = searchTitle.Replace(sceneID, "").Trim();
                searchResults.Add($"{Helper.GetSearchBaseURL(siteNum)}/scenes/{sceneID}");
            }

            string encodedTitle = searchTitle.Replace("'", "").Replace(",", "").Replace("& ", "");
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}&key2={encodedTitle}&next=1&page=0";
            var req = await HTTP.Request(searchUrl, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            var searchPageElements = HTML.ElementFromString(req.Content);

            var searchPagesMatch = Regex.Match(req.Content, @"(?<=pages:\s).*(?=])");
            int numSearchPages = searchPagesMatch.Success ? Math.Min(int.Parse(searchPagesMatch.Value), 10) : 1;

            for (int i = 0; i < numSearchPages; i++)
            {
                foreach (var searchResult in searchPageElements.SelectNodes("//a"))
                {
                    string sceneURL = searchResult.GetAttributeValue("href", "");
                    if (sceneURL.Contains("/scenes/") && !searchResults.Contains(sceneURL))
                    {
                        string urlID = Regex.Replace(sceneURL, ".*/", "");
                        string siteDisplay = searchResult.SelectSingleNode(".//i")?.InnerText.Trim();
                        string titleNoFormatting = searchResult.SelectSingleNode(".//p[@class='gen12 bold']")?.InnerText;
                        string curID = Helper.Encode(sceneURL);

                        if (titleNoFormatting?.Contains("...") == true)
                        {
                            searchResults.Add(sceneURL);
                        }
                        else
                        {
                            siteResults.Add(sceneURL);
                            string date = searchResult.SelectSingleNode(".//span[@class='gen11']/text()")?.InnerText.Trim();
                            string releaseDate = !string.IsNullOrEmpty(date) && date != "unknown" ? DateTime.ParseExact(date, "MMMM dd, yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd") : (searchDate.HasValue ? searchDate.Value.ToString("yyyy-MM-dd") : "");

                            result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } }, Name = $"{titleNoFormatting} [{siteDisplay}] {releaseDate}" });
                        }
                    }
                }
                if (numSearchPages > 1 && i + 1 != numSearchPages)
                {
                    searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}&key2={encodedTitle}&next=1&page={i + 1}";
                    req = await HTTP.Request(searchUrl, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                    searchPageElements = HTML.ElementFromString(req.Content);
                }
            }

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var url = sceneURL.Replace("/content/", "/scenes/").Replace("http:", "https:");
                if (url.Contains("/scenes/") && !url.Contains(".html") && !searchResults.Contains(url) && !siteResults.Contains(url))
                    searchResults.Add(url);
            }

            foreach (var sceneURL in searchResults)
            {
                var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                if (detailsPageElements == null) continue;

                string urlID = Regex.Replace(sceneURL, ".*/", "");
                string siteName = detailsPageElements.SelectSingleNode("//b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::b | //b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::a")?.InnerText.Trim();
                string subSite = detailsPageElements.SelectSingleNode("//p[contains(., 'Site:')]//following-sibling::a[@class='bold']")?.InnerText.Trim();
                string siteDisplay = !string.IsNullOrEmpty(siteName) ? (!string.IsNullOrEmpty(subSite) ? $"{siteName}/{subSite}" : siteName) : subSite;
                string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1").InnerText;
                string curID = Helper.Encode(sceneURL);
                string date = detailsPageElements.SelectSingleNode("//@datetime")?.GetAttributeValue("datetime", "").Trim();
                string releaseDate = !string.IsNullOrEmpty(date) && date != "unknown" ? DateTime.Parse(date).ToString("yyyy-MM-dd") : (searchDate.HasValue ? searchDate.Value.ToString("yyyy-MM-dd") : "");

                result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } }, Name = $"{titleNoFormatting} [{siteDisplay}] {releaseDate}" });
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1").InnerText;

            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='gen12']/div[contains(., 'Story')]") ?? detailsPageElements.SelectSingleNode("//div[@class='gen12']//div[@class='hideContent boxdesc' and contains(., 'Description')]") ?? detailsPageElements.SelectSingleNode("//div[@class='gen12']/div[contains(., 'Movie Description')]");
            movie.Overview = summaryNode?.InnerText.Split(new[] { "Story -", "---", "--" }, StringSplitOptions.RemoveEmptyEntries).Last().Trim();

            movie.AddStudio(detailsPageElements.SelectSingleNode("//b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::b | //b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::a | //p[contains(., 'Site:')]//following-sibling::a[@class='bold']")?.InnerText.Trim());

            var taglineNode = detailsPageElements.SelectSingleNode("//p[contains(., 'Site:')]//following-sibling::a[@class='bold'] | //b[contains(., 'Network')]//following-sibling::a | //p[contains(., 'Webserie:')]/a | //p[contains(., 'Movie:')]/a");
            if(taglineNode != null)
                movie.AddTag(taglineNode.InnerText.Trim());
            else
                movie.AddTag(movie.Studios.FirstOrDefault());

            var dateNode = detailsPageElements.SelectSingleNode("//span[contains(., 'Release date:')]");
            string date = dateNode?.InnerText.Replace("Release date:", "").Replace(", more updates...\n[Nav X]", "").Replace("* Movie Release", "").Trim() ?? sceneDate;
            if(!string.IsNullOrEmpty(date))
            {
                if (DateTime.TryParse(date, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[./b[contains(., 'Categories')]]//a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//h3[contains(., 'Cast')]//following::div[./p[contains(., 'No Profile')]]//span[@class]/text() | //h3[contains(., 'Cast')]//following::div//a[contains(@href, '/name/')]/img/@alt");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            if (detailsPageElements == null) return result;

            var imageNodes = detailsPageElements.SelectNodes("//img[@id='photoimg']/@src | //img[contains(@src, 'th8')]/@src | //img[contains(@data-original, 'th8')]/@data-original");
            if (imageNodes != null)
            {
                foreach(var image in imageNodes)
                    result.Add(new RemoteImageInfo { Url = image.GetAttributeValue(image.Name.Contains("data-") ? "data-original" : "src", "").Replace("/th8", "").Replace("-th8", "") });
            }

            var galleries = detailsPageElements.SelectNodes("//div[@id='galleriesoff']//div");
            if (galleries != null)
            {
                string id = Regex.Replace(sceneURL, ".*/", "");
                foreach(var gallery in galleries)
                {
                    string galleryID = gallery.GetAttributeValue("id", "").Replace("gallery", "");
                    string photoViewerURL = $"{Helper.GetSearchBaseURL(siteNum)}/sys/media_photos.php?s={id[0]}&scene={id.Substring(1)}&pic={galleryID}";
                    var photoPageElements = await HTML.ElementFromURL(photoViewerURL, cancellationToken);
                    if (photoPageElements != null)
                    {
                        imageNodes = photoPageElements.SelectNodes("//img[@id='photoimg']/@src | //img[contains(@src, 'th8')]/@src | //img[contains(@data-original, 'th8')]/@data-original");
                        if(imageNodes != null)
                        {
                            foreach(var image in imageNodes)
                                result.Add(new RemoteImageInfo { Url = image.GetAttributeValue(image.Name.Contains("data-") ? "data-original" : "src", "").Replace("/th8", "").Replace("-th8", "") });
                        }
                    }
                }
            }

            var cover = detailsPageElements.SelectSingleNode("//a[@class='pvideof']/@href");
            if (cover != null)
                result.Add(new RemoteImageInfo { Url = cover.GetAttributeValue("href", "") });

            var movieWrap = detailsPageElements.SelectSingleNode("//div[@id='moviewrap']//@src");
            if (movieWrap != null)
                result.Add(new RemoteImageInfo { Url = movieWrap.GetAttributeValue("src", "") });

            return result;
        }
    }
}
