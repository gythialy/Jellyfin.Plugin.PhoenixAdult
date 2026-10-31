using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteJulesJordan : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // Site search ANDs the words and the SEO file name differs from site titles,
            // so search the first two words, which are the actors.
            var searchQuery = Helper.GetSearchTitle(searchTitle, 2);

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchQuery);
            var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            // Current template: div.jj-content-card with a.jj-card-thumb, .jj-card-title and .jj-card-date.
            var searchResults = data.SelectNodesSafe("//div[contains(@class, 'jj-content-card')]");
            foreach (var searchResult in searchResults)
            {
                var sceneLink = searchResult.SelectSingleNode(".//a[contains(@class, 'jj-card-thumb')]");
                if (sceneLink == null)
                {
                    continue;
                }

                var sceneHref = sceneLink.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(sceneHref))
                {
                    continue;
                }

                string curID = Helper.Encode(sceneHref),
                    sceneName = searchResult.SelectSingleText(".//div[contains(@class, 'jj-card-title')]"),
                    scenePoster = searchResult.SelectSingleText(".//img[1]/@src");

                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = searchResult.SelectSingleText(".//img[1]/@alt");
                }

                var sceneDateNode = searchResult.SelectSingleNode(".//div[contains(@class, 'jj-card-date')]");
                var res = new RemoteSearchResult
                {
                    Name = sceneName,
                    ImageUrl = scenePoster,
                };

                if (sceneDateNode != null)
                {
                    var sceneDate = sceneDateNode.InnerText.Trim();
                    sceneDate = Regex.Replace(sceneDate, "Released\\s*:\\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
                    if (DateTime.TryParse(sceneDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                    {
                        res.PremiereDate = sceneDateObj;
                        curID += $"#{sceneDateObj.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
                    }
                }

                res.ProviderIds.Add(Plugin.Instance.Name, curID);
                result.Add(res);
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

            if (sceneID == null)
            {
                return result;
            }

            string sceneURL = Helper.Decode(sceneID[0]),
                sceneDate = string.Empty;

            if (sceneID.Length > 1)
            {
                sceneDate = sceneID[1];
            }

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;
            result.HasMetadata = true;

            // Current template: the title is in h1.
            var titleNode = sceneData.SelectSingleNode("//h1");
            result.Item.Name = System.Net.WebUtility.HtmlDecode(titleNode?.InnerText?.Trim());

            // Description: the scene description before the Categories block.
            var descNode = sceneData.SelectSingleNode("//div[contains(@class, 'scene-cats')]/preceding-sibling::div[1]");
            result.Item.Overview = descNode?.InnerText?.Trim();

            result.Item.AddStudio("Jules Jordan");

            if (!string.IsNullOrEmpty(sceneDate))
            {
                if (DateTime.TryParseExact(sceneDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    result.Item.PremiereDate = sceneDateObj;
                }
            }

            // sceneDate is only set when a scene id is passed in;
            // fall back to the embedded uploadDate.
            if (!result.Item.PremiereDate.HasValue)
            {
                var uploadDateMatch = System.Text.RegularExpressions.Regex.Match(
                    sceneData.InnerHtml ?? string.Empty,
                    "\"uploadDate\"\\s*:\\s*\"([^\"]+)\"");
                if (uploadDateMatch.Success
                    && DateTime.TryParse(uploadDateMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var uploadDate))
                {
                    result.Item.PremiereDate = uploadDate;
                }
            }

            // Genres: .scene-cats a.cat-tag.
            var genreNode = sceneData.SelectNodesSafe("//div[contains(@class, 'scene-cats')]//a[contains(@class, 'cat-tag')]");
            foreach (var genreLink in genreNode)
            {
                var genreName = genreLink.InnerText?.Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    result.Item.AddGenre(genreName);
                }
            }

            // Actors: the main block in .scene-meta (related cards also carry update_models).
            var actorsNode = sceneData.SelectNodesSafe("//div[contains(@class, 'scene-meta')]//span[contains(@class, 'update_models')]//a");
            foreach (var actorLink in actorsNode)
            {
                var actorName = actorLink.InnerText?.Trim();
                if (string.IsNullOrEmpty(actorName))
                {
                    continue;
                }

                var actor = new PersonInfo
                {
                    Name = actorName,
                };

                var href = actorLink.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(href))
                {
                    var actorPage = await HTML.ElementFromURL(href, cancellationToken).ConfigureAwait(false);
                    var actorPhotoNode = actorPage.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')]");
                    if (actorPhotoNode != null)
                    {
                        string actorPhoto = actorPhotoNode.GetAttributeValue("src0_3x", string.Empty);
                        if (string.IsNullOrEmpty(actorPhoto))
                        {
                            actorPhoto = actorPhotoNode.GetAttributeValue("src0", string.Empty);
                        }

                        if (string.IsNullOrEmpty(actorPhoto))
                        {
                            actorPhoto = actorPhotoNode.GetAttributeValue("src", string.Empty);
                        }

                        if (!string.IsNullOrEmpty(actorPhoto))
                        {
                            if (!actorPhoto.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                actorPhoto = Helper.GetSearchBaseURL(siteNum) + actorPhoto;
                            }

                            actor.ImageUrl = actorPhoto;
                        }
                    }
                }

                result.AddPerson(actor);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null || string.IsNullOrEmpty(item?.Name))
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            // Cover: og:image.
            var ogImage = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(ogImage))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = ogImage,
                    Type = ImageType.Primary,
                });
            }

            // Backdrop: the player poster, a full-size still of this scene;
            // the stdimage/thumbs classes belong to related cards at the bottom.
            var backdropUrl = sceneData.SelectSingleText("//video[@id='video-player']/@poster");
            if (string.IsNullOrEmpty(backdropUrl))
            {
                backdropUrl = ogImage;
            }

            if (!string.IsNullOrEmpty(backdropUrl))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = backdropUrl,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
