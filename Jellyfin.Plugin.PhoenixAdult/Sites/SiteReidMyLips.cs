using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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

namespace PhoenixAdult.Sites
{
    public class SiteReidMyLips : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.ToLower().Replace(" ", "-");
            var url = Helper.GetSearchSearchURL(siteNum) + encodedTitle + ".html";

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var titleNode = doc.DocumentNode.SelectSingleNode("//span[@class='update_title']");
                var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='availdate']");

                if (titleNode != null)
                {
                    var title = titleNode.InnerText.Trim();
                    var curID = Helper.Encode(url);
                    DateTime? releaseDateObj = null;

                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var date))
                    {
                        releaseDateObj = date;
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = title,
                        PremiereDate = releaseDateObj,
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
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//span[@class='update_title']")?.InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//span[@class='latest_update_description']")?.InnerText.Trim();
            movie.AddStudio("ReidMyLips");
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='availdate']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var date))
            {
                movie.PremiereDate = date;
                movie.ProductionYear = date.Year;
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//span[@class='update_tags']//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            result.People.Add(new PersonInfo { Name = "Riley Reid", Type = PersonKind.Actor });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var posterNodes = doc.DocumentNode.SelectNodes("//div[@class='update_image']//img");
                if (posterNodes != null)
                {
                    foreach (var poster in posterNodes)
                    {
                        var imgUrl = poster.GetAttributeValue("src0_2x", "");
                        if (string.IsNullOrEmpty(imgUrl))
                        {
                            imgUrl = poster.GetAttributeValue("src", "");
                        }

                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            if (!imgUrl.StartsWith("http"))
                            {
                                imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                            }
                            images.Add(new RemoteImageInfo { Url = imgUrl });
                        }
                    }
                }
            }

            return images;
        }
    }
}
