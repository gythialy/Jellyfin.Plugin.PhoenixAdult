using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteAlsAngels : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var httpResult = await HTTP.Request(Helper.GetSearchSearchURL(siteNum), HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            HtmlNodeCollection parent = null;

            if (searchDate.HasValue)
            {
                parent = detailsPageElements.SelectNodes($"//tr[.//span[@class='videodate' and contains(text(), \"{searchDate.Value.ToString("MMMM d, yyyy")}\")]]");
            }
            else if (!string.IsNullOrEmpty(searchTitle))
            {
                var match = Regex.Match(searchTitle, @"([a-z0-9\&\; ]+) (masturbation|photoshoot|interview|girl-girl action|pov lapdance)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    parent = detailsPageElements.SelectNodes($"//tr[.//h2[@class='videomodel' and contains(text(), \"{match.Groups[1].Value}\")] and .//span[@class='videotype' and contains(text(), \"{match.Groups[2].Value}\")]]");
                }
            }

            if (parent != null)
            {
                foreach (var elem in parent)
                {
                    string model = Regex.Replace(elem.SelectSingleNode(".//h2[@class='videomodel']")?.InnerText.Trim() ?? "", @"Models?: (.+)", "$1");
                    string genre = Regex.Replace(elem.SelectSingleNode(".//span[@class='videotype']")?.InnerText.Trim() ?? "", @"Video Type: (.+)", "$1");
                    string releaseDate = DateTime.Parse(Regex.Replace(elem.SelectSingleNode(".//span[@class='videodate']")?.InnerText.Trim() ?? "", @"Date: (.+)", "$1")).ToString("yyyy-MM-dd");
                    string sceneId = Regex.Replace(elem.SelectSingleNode(".//td[@class='videothumbnail']/a/img")?.GetAttributeValue("src", "").Trim() ?? "", @"graphics/videos/(.+)\.jpg", "$1");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{sceneId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{model} {genre} {releaseDate} [ALSAngels/{sceneId}]",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneId = providerIds[0];
            var match = Regex.Match(sceneId, @"([^0-9]+)([0-9-]+)");
            string modelId = match.Groups[1].Value;
            string sceneNum = match.Groups[2].Value;

            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/profiles/{modelId}.html#videoupdate";
            string sceneDate = providerIds[2];
            DateTime dateObject = DateTime.Parse(sceneDate);
            string dateString = dateObject.ToString("MMMM d, yyyy");
            string searchBaseUrl = Helper.GetSearchBaseURL(siteNum).Trim();

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var parentElement = detailsPageElements.SelectSingleNode($"//tr[.//span[@class='videodate' and contains(text(), \"{dateString}\")]]");

            string model = Regex.Replace(detailsPageElements.SelectSingleNode("//title")?.InnerText.Trim() ?? "", @"ALSAngels.com - (.+)", "$1", RegexOptions.IgnoreCase);
            string subject = Regex.Replace(parentElement.SelectSingleNode(".//span[@class='videotype']")?.InnerText.Trim() ?? "", @"Video Type: (.+)", "$1", RegexOptions.IgnoreCase);

            var movie = (Movie)result.Item;
            movie.Name = $"{model} #{int.Parse(sceneNum)}: {subject}";
            movie.Overview = parentElement.SelectSingleNode(".//span[@class='videodescription']")?.InnerText.Trim();
            movie.AddStudio("ALSAngels");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });
            movie.PremiereDate = dateObject;
            movie.ProductionYear = dateObject.Year;
            movie.AddGenre(subject);

            string actorPhotoUrl = detailsPageElements.SelectSingleNode(".//div[@id='modelbioheadshot']/img")?.GetAttributeValue("src", "").Replace("..", searchBaseUrl);
            result.People.Add(new PersonInfo { Name = model, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneId = providerIds[0];
            var match = Regex.Match(sceneId, @"([^0-9]+)([0-9-]+)");
            string modelId = match.Groups[1].Value;
            string sceneDate = providerIds[2];
            DateTime dateObject = DateTime.Parse(sceneDate);
            string dateString = dateObject.ToString("MMMM d, yyyy");
            string searchBaseUrl = Helper.GetSearchBaseURL(siteNum).Trim();

            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/profiles/{modelId}.html#videoupdate";
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
if (!httpResult.IsOK) return images;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var parentElement = detailsPageElements.SelectSingleNode($"//tr[.//span[@class='videodate' and contains(text(), \"{dateString}\")]]");

            var imageNodes = parentElement.SelectNodes(".//td[@class='videothumbnail']//img | .//td[@class='videothumbnail']//a");
            if (imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = (img.GetAttributeValue("src", "") ?? img.GetAttributeValue("href", "")).Replace("..", searchBaseUrl);
                    images.Add(new RemoteImageInfo { Url = imageUrl });
                }
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
