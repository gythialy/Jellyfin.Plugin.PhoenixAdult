using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteJacquieEtMichel : IProviderBase
    {
        private const string SiteName = "Jacquie Et Michel TV";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // TV 站搜索。注意：不能加 label=scene 过滤，否则标题词会漏结果
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}";
            var doc = await HTML.ElementFromURL(searchUrl, cancellationToken);
            if (doc == null)
            {
                return result;
            }

            var nodes = doc.SelectNodes("//a[contains(@class, 'content-card__wrapper')]");
            if (nodes == null)
            {
                return result;
            }

            foreach (var node in nodes)
            {
                var card = node.ParentNode;
                var titleNode = card?.SelectSingleNode(".//div[contains(@class, 'content-card__title')]");
                if (titleNode == null)
                {
                    continue;
                }

                var titleNoFormatting = titleNode.InnerText.Trim();
                var href = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var releaseDate = string.Empty;
                var infoNode = card.SelectSingleNode(".//div[contains(@class, 'content-card__infos')]");
                if (infoNode != null && DateTime.TryParse(infoNode.InnerText.Trim(), out var parsedDate))
                {
                    releaseDate = parsedDate.ToString("yyyy-MM-dd");
                }

                var image = card.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
                var curID = Helper.Encode(href);

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = $"{titleNoFormatting} [{SiteName}] {releaseDate}",
                    ImageUrl = image,
                });
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

            var sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken);
            if (doc == null)
            {
                return result;
            }

            result.Item.ExternalId = sceneUrl;
            result.HasMetadata = true;
            result.Item.OfficialRating = "XXX";
            result.Item.AddStudio(SiteName);

            // 场景页内嵌 JSON-LD VideoObject：标题/日期/描述/流派/演员最可靠
            var jsonLdNode = doc.SelectSingleNode("//script[@type='application/ld+json']");
            if (jsonLdNode != null)
            {
                var json = JObject.Parse(jsonLdNode.InnerText);
                var video = (JObject)(json["@graph"]?.First ?? json);
                if (video == null)
                {
                    return result;
                }

                var title = (string)video["name"];
                if (string.IsNullOrEmpty(title))
                {
                    return result;
                }

                result.Item.Name = title;

                var description = (string)video["description"];
                if (!string.IsNullOrEmpty(description))
                {
                    result.Item.Overview = description;
                }

                if (DateTime.TryParse((string)video["datePublished"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    result.Item.PremiereDate = parsedDate;
                }

                if (video["keywords"] is JArray keywords)
                {
                    foreach (var keyword in keywords)
                    {
                        var genreName = ((string)keyword)?.Trim();
                        if (!string.IsNullOrEmpty(genreName))
                        {
                            result.Item.AddGenre(genreName);
                        }
                    }
                }

                if (video["actor"] is JArray actors)
                {
                    foreach (var actor in actors)
                    {
                        var actorName = (string)actor["name"];
                        if (!string.IsNullOrEmpty(actorName))
                        {
                            result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                        }
                    }
                }

                return result;
            }

            // fallback: 无 JSON-LD 时用 HTML 选择器
            result.Item.Name = doc.SelectSingleNode("//h1[contains(@class, 'content-detail__title')]")?.InnerText.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(result.Item.Name))
            {
                return result;
            }

            var overview = doc.SelectSingleNode("//div[contains(@class, 'content-detail__description') and not(contains(@class, 'content-detail__description--link'))]");
            if (overview != null)
            {
                result.Item.Overview = overview.InnerText.Trim();
            }

            var infoRows = doc.SelectNodes("//div[contains(@class, 'content-detail__infos__el')]");
            if (infoRows != null)
            {
                foreach (var row in infoRows)
                {
                    var label = row.SelectSingleNode(".//div[contains(@class, 'content-detail__infos__title')]")?.InnerText.Trim();
                    if (string.IsNullOrEmpty(label) || !label.Contains("Publication", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dateText = row.SelectSingleNode(".//p[contains(@class, 'content-detail__description')]")?.InnerText.Trim();
                    if (!string.IsNullOrEmpty(dateText) && DateTime.TryParse(dateText, out var parsedDate))
                    {
                        result.Item.PremiereDate = parsedDate;
                    }

                    break;
                }
            }

            var genres = doc.SelectNodes("//li[contains(@class, 'content-detail__tag')]//a");
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    var genreName = genre.InnerText.Trim();
                    if (string.IsNullOrEmpty(genreName))
                    {
                        continue;
                    }

                    if (genreName.Equals("Sodomy", StringComparison.OrdinalIgnoreCase))
                    {
                        genreName = "Anal";
                    }

                    result.Item.AddGenre(genreName);
                }
            }

            var actorLinks = doc.SelectNodes("//a[starts-with(@href, '/en/actors/')]");
            if (actorLinks != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var actor in actorLinks)
                {
                    var actorName = actor.InnerText.Trim();
                    if (string.IsNullOrEmpty(actorName) || actorName.Equals("Our actors", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (seen.Add(actorName))
                    {
                        result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken);
            if (doc == null)
            {
                return result;
            }

            var videoNode = doc.SelectSingleNode("//video");
            var img = videoNode?.GetAttributeValue("poster", string.Empty) ?? string.Empty;
            if (!string.IsNullOrEmpty(img))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = img,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = img,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
