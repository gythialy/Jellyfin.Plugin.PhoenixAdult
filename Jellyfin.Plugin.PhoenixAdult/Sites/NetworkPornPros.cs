using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public class NetworkPornPros : IProviderBase
    {
        private static readonly Dictionary<string, string[]> Genres = new Dictionary<string, string[]>
        {
            { "Lubed", new[] { "Lube", "Raw", "Wet" } },
            { "Holed", new[] { "Anal", "Ass" } },
            { "POVD", new[] { "Gonzo", "Pov" } },
            { "MassageCreep", new[] { "Massage", "Oil" } },
            { "DeepThroatLove", new[] { "Blowjob", "Deep Throat" } },
            { "PureMature", new[] { "MILF", "Mature" } },
            { "Cum4K", new[] { "Creampie" } },
            { "GirlCum", new[] { "Orgasms", "Girl Orgasm", "Multiple Orgasms" } },
            { "PassionHD", new[] { "Hardcore" } },
            { "BBCPie", new[] { "Interracial", "BBC", "Creampie" } },
            { "Facials4k", new[] { "Facial" } },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // Nuxt/API 站点：场景 URL = {base}/video/{标题slug}，slug 不含演员名。
            // 文件名 Site.YY.MM.DD.Actors.Title 的标题在末尾 → 从末尾 4-1 词生成 slug 候选逐个试。
            var words = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var candidates = new List<string> { searchTitle };
            for (var n = Math.Min(4, words.Length); n >= 1; n--)
            {
                candidates.Add(string.Join(" ", words.Skip(words.Length - n)));
            }

            // API 只认无 www 域名 + x-site header
            var baseUri = new Uri(Helper.GetSearchBaseURL(siteNum));
            var apiHost = baseUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? baseUri.Host.Substring(4) : baseUri.Host;

            foreach (var candidate in candidates)
            {
                var slug = candidate
                    .Replace("'", "-", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
                    .ToLowerInvariant();

                var apiURL = new Uri($"https://{apiHost}/api/releases/{slug}");
                var headers = new Dictionary<string, string> { { "x-site", apiHost } };
                var httpResult = await HTTP.Request(apiURL.AbsoluteUri, cancellationToken, headers).ConfigureAwait(false);
                if (!httpResult.IsOK)
                {
                    continue;
                }

                JObject release;
                try
                {
                    release = JObject.Parse(httpResult.Content);
                }
                catch (Exception e)
                {
                    Logger.Error($"PornPros API parse error for {slug}: {e.Message}");
                    continue;
                }

                if (release["title"] == null)
                {
                    continue; // 404 返回 {"message":"Not found"}
                }

                string curID = Helper.Encode($"/video/{slug}"),
                    sceneName = (string)release["title"];
                DateTime? sceneDateObj = null;
                if (release["releasedAt"] != null && DateTime.TryParse((string)release["releasedAt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    sceneDateObj = parsedDate;
                }

                var res = new RemoteSearchResult
                {
                    Name = sceneName,
                    ImageUrl = (string)release["posterUrl"],
                    PremiereDate = sceneDateObj,
                };
                res.ProviderIds.Add(Plugin.Instance.Name, curID);

                result.Add(res);
                break;
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

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var subSite = Helper.GetSearchSiteName(siteNum);

            // Nuxt/API 站点优先：/api/releases/{slug} 返回完整 JSON（标题/日期/演员/图片）
            var slug = new Uri(sceneURL).AbsolutePath.TrimEnd('/').Split('/').LastOrDefault();
            if (!string.IsNullOrEmpty(slug))
            {
                var baseUri = new Uri(Helper.GetSearchBaseURL(siteNum));
                var apiHost = baseUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? baseUri.Host.Substring(4) : baseUri.Host;
                var apiURL = $"https://{apiHost}/api/releases/{slug}";
                var headers = new Dictionary<string, string> { { "x-site", apiHost } };
                var apiHttp = await HTTP.Request(apiURL, cancellationToken, headers).ConfigureAwait(false);
                if (apiHttp.IsOK)
                {
                    JObject apiRelease;
                    try
                    {
                        apiRelease = JObject.Parse(apiHttp.Content);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"PornPros API parse error in Update for {slug}: {e.Message}");
                        apiRelease = null;
                    }

                    if (apiRelease != null && apiRelease["title"] != null)
                    {
                        result.Item.ExternalId = sceneURL;

                        result.Item.Name = (string)apiRelease["title"];
                        var apiDescription = (string)apiRelease["description"];
                        result.Item.Overview = string.IsNullOrEmpty(apiDescription) ? string.Empty : apiDescription.Replace("\r\n", "\n", StringComparison.OrdinalIgnoreCase);

                        result.Item.AddStudio("Porn Pros");
                        result.Item.AddStudio(subSite);

                        if (apiRelease["releasedAt"] != null
                            && DateTime.TryParse((string)apiRelease["releasedAt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var apiDateObj))
                        {
                            result.Item.PremiereDate = apiDateObj;
                            result.Item.ProductionYear = apiDateObj.Year;
                        }

                        if (Genres.ContainsKey(subSite))
                        {
                            foreach (var genreLink in Genres[subSite])
                            {
                                result.Item.AddGenre(genreLink);
                            }
                        }

                        if (apiRelease["actors"] is JArray apiActors)
                        {
                            foreach (var actorToken in apiActors)
                            {
                                var actorName = (string)actorToken["name"];
                                if (!string.IsNullOrEmpty(actorName))
                                {
                                    result.AddPerson(new PersonInfo { Name = actorName });
                                }
                            }
                        }

                        return result;
                    }
                }
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;

            var title = sceneData.SelectSingleText("//h1");
            result.Item.Name = title;
            var description = sceneData.SelectSingleText("//div[contains(@id, 'description')]");
            if (string.IsNullOrEmpty(description))
            {
                // povd
                description = sceneData.SelectSingleText("//div[contains(@class, 'scene-info')]//div[@class='w-full flex flex-row space-x-4 items-start']/span");
            }

            result.Item.Overview = description;

            result.Item.AddStudio("Porn Pros");
            result.Item.AddStudio(subSite);

            string date = sceneData.SelectSingleText("//div[@class='d-inline d-lg-block mb-1']/span"),
                sceneDate = string.Empty,
                dateFormat = string.Empty;
            if (!string.IsNullOrEmpty(date))
            {
                sceneDate = date;
                dateFormat = "MMMM dd, yyyy";
            }
            else
            {
                if (sceneID.Length > 1)
                {
                    sceneDate = sceneID[1];
                    dateFormat = "yyyy-MM-dd";
                }
            }

            if (!string.IsNullOrEmpty(sceneDate))
            {
                if (DateTime.TryParseExact(sceneDate, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    result.Item.PremiereDate = sceneDateObj;
                }
            }
            else
            {
                Logger.Warning($"Could not get date for '{title}'. Pulling for Metadata API");

                // get date from MetadataApi
                var metadataApiProvider = Helper.GetMetadataAPIProvider();
                var searchResults = await metadataApiProvider.Search(new int[] { 48, 0 }, title, null, cancellationToken);

                if (searchResults.Any())
                {
                    result.Item.PremiereDate = searchResults[0].PremiereDate;
                }
            }

            if (Genres.ContainsKey(subSite))
            {
                foreach (var genreLink in Genres[subSite])
                {
                    var genreName = genreLink;

                    result.Item.AddGenre(genreName);
                }
            }

            var actorsNode = sceneData.SelectNodesSafe("//div[contains(@class, 'pt-md')]//a[contains(@href, '/girls/')]");
            if (actorsNode.Any())
            {
                foreach (var actorLink in actorsNode)
                {
                    var actorName = actorLink.InnerText;

                    result.AddPerson(new PersonInfo
                    {
                        Name = actorName,
                    });
                }
            }
            else
            {
                actorsNode = sceneData.SelectNodesSafe("//div[contains(@class, 'scene-info')]//a");
                foreach (var actorLink in actorsNode)
                {
                    var actorName = actorLink.InnerText;

                    result.AddPerson(new PersonInfo
                    {
                        Name = actorName,
                    });
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

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            // Nuxt/API 站点优先：posterUrl + thumbUrls
            var slug = new Uri(sceneURL).AbsolutePath.TrimEnd('/').Split('/').LastOrDefault();
            if (!string.IsNullOrEmpty(slug))
            {
                var baseUri = new Uri(Helper.GetSearchBaseURL(siteNum));
                var apiHost = baseUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? baseUri.Host.Substring(4) : baseUri.Host;
                var apiURL = $"https://{apiHost}/api/releases/{slug}";
                var headers = new Dictionary<string, string> { { "x-site", apiHost } };
                var apiHttp = await HTTP.Request(apiURL, cancellationToken, headers).ConfigureAwait(false);
                if (apiHttp.IsOK)
                {
                    JObject apiRelease;
                    try
                    {
                        apiRelease = JObject.Parse(apiHttp.Content);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"PornPros API parse error in GetImages for {slug}: {e.Message}");
                        apiRelease = null;
                    }

                    if (apiRelease != null && apiRelease["title"] != null)
                    {
                        var poster = (string)apiRelease["posterUrl"];
                        if (!string.IsNullOrEmpty(poster))
                        {
                            result.Add(new RemoteImageInfo
                            {
                                Url = poster,
                                Type = ImageType.Primary,
                            });
                            result.Add(new RemoteImageInfo
                            {
                                Url = poster,
                                Type = ImageType.Backdrop,
                            });
                        }

                        if (apiRelease["thumbUrls"] is JArray thumbUrls)
                        {
                            foreach (var thumbToken in thumbUrls)
                            {
                                var thumbUrl = (string)thumbToken;
                                if (!string.IsNullOrEmpty(thumbUrl))
                                {
                                    result.Add(new RemoteImageInfo
                                    {
                                        Url = thumbUrl,
                                        Type = ImageType.Primary,
                                    });
                                    result.Add(new RemoteImageInfo
                                    {
                                        Url = thumbUrl,
                                        Type = ImageType.Backdrop,
                                    });
                                }
                            }
                        }

                        return result;
                    }
                }
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            var img = sceneData.SelectSingleText("//video[@id='player']/@poster");
            if (!string.IsNullOrEmpty(img))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = "https:" + img;
                }

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

            var gallery = sceneData.SelectNodesSafe("//div[@id='trailer_player']//div[@class='hidden w-full lg:flex flex-row mt-2']/a/img");
            foreach (var image in gallery)
            {
                var url = image.Attributes["src"].Value;
                var uri = new Uri(url);
                result.Add(new RemoteImageInfo
                {
                    Url = uri.GetLeftPart(UriPartial.Path),
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = uri.GetLeftPart(UriPartial.Path),
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
