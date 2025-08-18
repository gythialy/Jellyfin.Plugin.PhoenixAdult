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
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using MediaBrowser.Model.Entities;


#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteLittleCaprice : IProviderBase
    {
        private const string SiteName = "LittleCaprice";
        private const string BaseUrl = "https://www.littlecaprice.com";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var searchUrl = $"{BaseUrl}/?s={searchTitle.Replace(" ", "+")}";
            var http = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            var searchResults = new List<RemoteSearchResult>();
            var nodes = doc.DocumentNode.SelectNodes("//div[@id='left-area']/article");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var titleNoFormatting = node.SelectSingleNode(".//h2[@class='entry-title']/a").InnerText.Trim();
                    var curId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(node.SelectSingleNode(".//h2[@class='entry-title']/a").GetAttributeValue("href", string.Empty)));
                    var releaseDate = DateTime.Parse(node.SelectSingleNode(".//span[@class='published']").InnerText.Trim()).ToString("yyyy-MM-dd");


                    searchResults.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{SiteName}] {releaseDate}",
                    });
                }
            }

            return searchResults;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var http = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            var galleryDoc = new HtmlDocument();
            galleryDoc.LoadHtml(http.Content);
            var detailsDoc = galleryDoc;

            var videoPageLink = galleryDoc.DocumentNode.SelectSingleNode("//a[@class='et_pb_button button' and contains(@href, 'video')]");
            if (videoPageLink != null)
            {
                var videoPageUrl = videoPageLink.GetAttributeValue("href", string.Empty);
                if (!videoPageUrl.StartsWith("http"))
                {
                    videoPageUrl = $"{BaseUrl}{videoPageUrl}";
                }
                detailsDoc = new HtmlDocument();
                var videoHttp = await HTTP.Request(videoPageUrl, HttpMethod.Get, cancellationToken);
                detailsDoc.LoadHtml(videoHttp.Content);
            }

            var metadataResult = new MetadataResult<Movie>
            {
                Item = new Movie(),
                HasMetadata = true,
            };

            metadataResult.Item.AddStudio(SiteName);

            var summaryNode = detailsDoc.DocumentNode.SelectSingleNode("//div[@class='desc-text']");
            if (summaryNode != null)
            {
                metadataResult.Item.Overview = summaryNode.InnerText.Trim();
            }

            var genres = detailsDoc.DocumentNode.SelectNodes("//div[@class='project-tags']/div[@class='list']/a") ?? galleryDoc.DocumentNode.SelectNodes("//div[@class='project-tags']/div[@class='list']/a");
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    metadataResult.Item.AddGenre(genre.InnerText.ToLower());
                }
            }

            var attributes = detailsDoc.DocumentNode.SelectSingleNode("//div[@id='main-project-content']").GetAttributeValue("class", string.Empty).Split(' ');
            var tagline = SiteName;
            if (attributes.Contains("category_buttmuse"))
            {
                tagline = "Buttmuse";
            }
            else if (attributes.Contains("category_caprice-divas"))
            {
                tagline = "Caprice Divas";
            }
            else if (attributes.Contains("category_nasstyx"))
            {
                tagline = "NasstyX";
            }
            else if (attributes.Contains("category_povdreams"))
            {
                tagline = "POVDreams";
            }
            else if (attributes.Contains("category_streetfuck"))
            {
                tagline = "Streetfuck";
            }
            else if (attributes.Contains("category_superprivatex"))
            {
                tagline = "SuperprivateX";
            }
            else if (attributes.Contains("category_wecumtoyou"))
            {
                tagline = "Wecumtoyou";
            }
            else if (attributes.Contains("category_xpervo"))
            {
                tagline = "Xpervo";
            }
            metadataResult.Item.Tagline = tagline;

            var title = detailsDoc.DocumentNode.SelectSingleNode("//div[@class='project-details']//h1").InnerText.Trim();
            if (title.ToLower().StartsWith(tagline.ToLower()))
            {
                title = title.Substring(tagline.Length);
            }
            metadataResult.Item.Name = title;

            var date = detailsDoc.DocumentNode.SelectSingleNode("//div[@class='relese-date']").InnerText.Trim().Split(':')[1];
            if (!string.IsNullOrEmpty(date))
            {
                metadataResult.Item.PremiereDate = DateTime.Parse(date);
            }

            var actors = detailsDoc.DocumentNode.SelectNodes("//div[@class='project-models']//a");
            if (actors != null)
            {
                if (actors.Count == 3)
                {
                    metadataResult.Item.AddGenre("Threesome");
                }
                if (actors.Count == 4)
                {
                    metadataResult.Item.AddGenre("Foursome");
                }
                if (actors.Count > 4)
                {
                    metadataResult.Item.AddGenre("Orgy");
                }

                foreach (var actor in actors)
                {
                    var actorName = actor.InnerText.Trim();
                    if (actorName == "LittleCaprice")
                    {
                        actorName = "Little Caprice";
                    }

                    var actorPhotoUrl = string.Empty;
                    try
                    {
                        var actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                        if (!actorPageUrl.StartsWith("http"))
                        {
                            actorPageUrl = $"{BaseUrl}{actorPageUrl}";
                        }
                        var actorDoc = new HtmlDocument();
                        var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                        actorDoc.LoadHtml(actorHttp.Content);
                        actorPhotoUrl = actorDoc.DocumentNode.SelectSingleNode("//img[@class='img-poster']").GetAttributeValue("src", string.Empty);
                        if (!actorPhotoUrl.StartsWith("http"))
                        {
                            actorPhotoUrl = $"{BaseUrl}{actorPhotoUrl}";
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                    metadataResult.AddPerson(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
                }
            }

            return metadataResult;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var galleryDoc = new HtmlDocument();
            var galleryHttp = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            galleryDoc.LoadHtml(galleryHttp.Content);
            var detailsDoc = galleryDoc;

            var videoPageLink = galleryDoc.DocumentNode.SelectSingleNode("//a[@class='et_pb_button button' and contains(@href, 'video')]");
            if (videoPageLink != null)
            {
                var videoPageUrl = videoPageLink.GetAttributeValue("href", string.Empty);
                if (!videoPageUrl.StartsWith("http"))
                {
                    videoPageUrl = $"{BaseUrl}{videoPageUrl}";
                }
                detailsDoc = new HtmlDocument();
                var videoHttp = await HTTP.Request(videoPageUrl, HttpMethod.Get, cancellationToken);
                detailsDoc.LoadHtml(videoHttp.Content);
            }

            var art = new List<string>();
            try
            {
                var detailsPageOgImage = detailsDoc.DocumentNode.SelectSingleNode("//meta[@property='og:image']").GetAttributeValue("content", string.Empty);
                art.Add(detailsPageOgImage);
            }
            catch
            {
                // ignored
            }

            try
            {
                var galleryPageOgImage = galleryDoc.DocumentNode.SelectSingleNode("//meta[@property='og:image']").GetAttributeValue("content", string.Empty);
                art.Add(galleryPageOgImage);
            }
            catch
            {
                // ignored
            }

            var galleryPhotos = galleryDoc.DocumentNode.SelectNodes("//div[@class='gallery spotlight-group']/img/@src");
            if (galleryPhotos != null)
            {
                foreach (var galleryPhoto in galleryPhotos)
                {
                    try
                    {
                        var photoUrl = galleryPhoto.GetAttributeValue("src", string.Empty);
                        if (!photoUrl.StartsWith("http"))
                        {
                            photoUrl = $"{BaseUrl}{photoUrl}";
                        }
                        art.Add(photoUrl);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            var list = new List<RemoteImageInfo>();
            foreach (var imageUrl in art)
            {
                list.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            }

            return list;
        }

        private static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.IsNullOrEmpty(target) ? 0 : target.Length;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            var distance = new int[source.Length + 1, target.Length + 1];

            for (var i = 0; i <= source.Length; distance[i, 0] = i++)
            {
            }

            for (var j = 0; j <= target.Length; distance[0, j] = j++)
            {
            }

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + cost);
                }
            }

            return distance[source.Length, target.Length];
        }
    }
}
