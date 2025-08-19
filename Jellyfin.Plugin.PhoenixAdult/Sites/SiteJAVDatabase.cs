using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers.Utils;
using MediaBrowser.Model.Entities;


#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteJavDatabase : IProviderBase
    {
        private const string SiteName = "JAVDatabase";
        private const string BaseUrl = "https://www.javdatabase.com";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var searchJavId = string.Empty;
            var splitSearchTitle = searchTitle.Split(' ');

            if (splitSearchTitle[0].StartsWith("3dsvr"))
            {
                splitSearchTitle[0] = splitSearchTitle[0].Replace("3dsvr", "dsvr");
            }
            else if (splitSearchTitle[0].StartsWith("13dsvr"))
            {
                splitSearchTitle[0] = splitSearchTitle[0].Replace("13dsvr", "dsvr");
            }

            if (splitSearchTitle.Length > 1)
            {
                if (int.TryParse(splitSearchTitle[1], out _))
                {
                    searchJavId = $"{splitSearchTitle[0]}-{splitSearchTitle[1]}";
                }
            }

            if (!string.IsNullOrEmpty(searchJavId))
            {
                searchTitle = searchJavId;
            }

            var searchUrl = $"{BaseUrl}/movies/{searchTitle.Replace(" ", "-").ToLower()}/";
            var doc = await HTML.ElementFromURL(searchUrl, cancellationToken);

            var searchResults = new List<RemoteSearchResult>();
            var nodes = doc.SelectNodes("//div[contains(@class, 'card h-100')]");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var titleNoFormatting = node.SelectSingleNode(".//div[@class='mt-auto']/a").InnerText.Trim();
                    var javId = node.SelectSingleNode(".//p//a[contains(@class, 'cut-text')]").InnerText.Trim();
                    var sceneUrl = node.SelectSingleNode(".//p//a[contains(@class, 'cut-text')]").GetAttributeValue("href", string.Empty);
                    var curId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sceneUrl));

                    var date = node.SelectNodes(".//div[@class='mt-auto']/text()");
                    var releaseDate = string.Empty;
                    if (date != null)
                    {
                        releaseDate = DateTime.Parse(date[1].InnerText.Trim()).ToString("yyyy-MM-dd");
                    }
                    else if (searchDate.HasValue)
                    {
                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                    }

                    var displayDate = !string.IsNullOrEmpty(releaseDate) ? releaseDate : string.Empty;

                    var score = 100 - LevenshteinDistance(searchJavId.ToLower(), javId.ToLower());

                    searchResults.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"[{javId}] {displayDate} {titleNoFormatting}",
                    });
                }
            }

            return searchResults;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken);

            var metadataResult = new MetadataResult<BaseItem>
            {
                Item = new Movie(),
                HasMetadata = true,
            };

            var javId = doc.SelectSingleNode("//meta[@property='og:title']").GetAttributeValue("content", string.Empty).Trim();
            var title = doc.SelectSingleNode("//*[text()='Title: ']/following-sibling::text()").InnerText.Trim();

            foreach (var censoredWord in CensoredWordsDb)
            {
                if (title.Contains(censoredWord.Key))
                {
                    title = title.Replace(censoredWord.Key, censoredWord.Value);
                }
            }

            if (title.Length > 80)
            {
                metadataResult.Item.Name = $"[{javId.ToUpper()}] {title}";
                metadataResult.Item.Overview = title;
            }
            else
            {
                metadataResult.Item.Name = $"[{javId.ToUpper()}] {title}";
            }

            var studioNode = doc.SelectSingleNode("//*[text()='Studio: ']/following-sibling::span/a");
            if (studioNode != null)
            {
                var studioClean = studioNode.InnerText.Trim();
                foreach (var censoredWord in CensoredWordsDb)
                {
                    if (studioClean.Contains(censoredWord.Key))
                    {
                        studioClean = studioClean.Replace(censoredWord.Key, censoredWord.Value);
                    }
                }

                metadataResult.Item.AddStudio(studioClean);
            }

            var date = doc.SelectSingleNode("//*[text()='Release Date: ']/following-sibling::text()[1]").InnerText;
            if (!string.IsNullOrEmpty(date))
            {
                metadataResult.Item.PremiereDate = DateTime.Parse(date);
            }

            var genres = doc.SelectNodes("//*[text()='Genre(s): ']/following-sibling::*/a");
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    metadataResult.Item.AddGenre(genre.InnerText.Trim());
                }
            }

            var actors = doc.SelectNodes("//*[text()='Idol(s)/Actress(es): ']/following-sibling::span/a");
            if (actors != null)
            {
                foreach (var actor in actors)
                {
                    var actorName = actor.InnerText.Trim();
                    var actorPhotoUrl = actor.GetAttributeValue("href", string.Empty).Replace("melody-marks", "melody-hina-marks");
                    var actorDoc = await HTML.ElementFromURL(actorPhotoUrl, cancellationToken);
                    if (actorDoc.InnerText.Contains("unknown."))
                    {
                        actorPhotoUrl = string.Empty;
                    }

                    if (!ActorsCorrectionDb.ContainsKey(javId) || ActorsCorrectionDb[javId].Contains(actorName, StringComparer.OrdinalIgnoreCase))
                    {
                        metadataResult.AddPerson(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
                    }
                }
            }

            var directorNode = doc.SelectSingleNode("//*[text()='Director: ']/following-sibling::span/a");
            if (directorNode != null)
            {
                var directorName = directorNode.InnerText.Trim();
                metadataResult.AddPerson(new PersonInfo { Name = directorName, Type = PersonKind.Director });
            }

            foreach (var actor in SceneActorsDb.Where(x => x.Value.Contains(javId)).Select(x => x.Key))
            {
                metadataResult.AddPerson(new PersonInfo { Name = actor, Type = PersonKind.Actor });
            }

            return metadataResult;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken);

            var xpaths = new[]
            {
                "//tr[@class='moviecovertb']//img/@src",
                "//div/div[./h2[contains(., 'Images')]]/a/@href",
            };

            var art = new List<string>();
            foreach (var xpath in xpaths)
            {
                var nodes = doc.SelectNodes(xpath);
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        art.Add(node.GetAttributeValue("src", string.Empty));
                    }
                }
            }

            var javId = doc.SelectSingleNode("//meta[@property='og:title']").GetAttributeValue("content", string.Empty).Trim();
            foreach (var crossSite in CrossSiteDb)
            {
                if (crossSite.Value.Contains(javId, StringComparer.OrdinalIgnoreCase))
                {
                    javId = javId.Replace(crossSite.Value[0], crossSite.Key);
                    break;
                }
            }

            var numLen = javId.Split('-').Last().Length;
            if (numLen < 3 && !IgnoreList.Contains(javId.Split('-')[0], StringComparer.OrdinalIgnoreCase))
            {
                for (var i = 1; i < 4 - numLen; i++)
                {
                    javId = $"{javId.Split('-')[0]}-0{javId.Split('-').Last()}";
                }
            }

            var javBusUrl = $"https://www.javbus.com/{javId}";
            var javBusDoc = await HTML.ElementFromURL(javBusUrl, cancellationToken);

            if (javBusDoc.InnerText.Contains("404 Page"))
            {
                var date = doc.SelectSingleNode("//*[text()='Release Date: ']/following-sibling::text()[1]").InnerText;
                if (!string.IsNullOrEmpty(date))
                {
                    javBusUrl = $"{javBusUrl}_{DateTime.Parse(date):yyyy-MM-dd}";
                    javBusDoc = await HTML.ElementFromURL(javBusUrl, cancellationToken);
                }
            }

            if (!javBusDoc.InnerText.Contains("404 Page"))
            {
                var javBusXpaths = new[]
                {
                    "//a[contains(@href, '/cover/')]/@href",
                    "//a[@class='sample-box']/@href",
                };

                foreach (var xpath in javBusXpaths)
                {
                    var nodes = javBusDoc.SelectNodes(xpath);
                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                        {
                            var poster = node.GetAttributeValue("href", string.Empty);
                            if (!poster.StartsWith("http"))
                            {
                                poster = $"https://www.javbus.com{poster}";
                            }

                            if (!poster.Contains("nowprinting") && !art.Contains(poster))
                            {
                                art.Add(poster);
                            }
                        }
                    }
                }

                var coverImageNode = javBusDoc.SelectSingleNode("//a[contains(@href, '/cover/')]/@href|//img[contains(@src, '/sample/')]/@src");
                if (coverImageNode != null)
                {
                    var coverImageCode = coverImageNode.GetAttributeValue("href", string.Empty).Split('/').Last().Split('.')[0].Split('_')[0];
                    var imageHost = string.Join("/", coverImageNode.GetAttributeValue("href", string.Empty).Split('/').Reverse().Skip(2).Reverse());
                    var coverImage = $"{imageHost}/thumb/{coverImageCode}.jpg";
                    if (coverImage.Count(c => c == '/') == 1)
                    {
                        coverImage = coverImage.Replace("thumb", "thumbs");
                    }

                    if (!coverImage.StartsWith("http"))
                    {
                        coverImage = $"https://www.javbus.com{coverImage}";
                    }

                    art.Add(coverImage);
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

        private static readonly Dictionary<string, string[]> SceneActorsDb = new Dictionary<string, string[]>
        {
            // ... (omitted for brevity)
        };

        private static readonly Dictionary<string, string[]> ActorsCorrectionDb = new Dictionary<string, string[]>
        {
            { "JUTA-132", new[] { "Rie Matsuo" } },
            { "CRDD-007", new[] { "Alicia Williams", "Jillian Janson", "Pamela Morrison" } },
        };

        private static readonly Dictionary<string, string> CensoredWordsDb = new Dictionary<string, string>
        {
            { "A***e", "Abuse" },
            { "A*****t", "Assault" },
            { "B******y", "Brutally" },
            { "B***d", "Blood" },
            { "C***d", "Child" },
            { "C*ck", "Cock" },
            { "Cum-D***king", "Cum-Drinking" },
            { "D******e", "Disgrace" },
            { "D***k ", "Drunk " },
            { "D***k It", "Drink It" },
            { "D***k-", "Drunk-" },
            { "D***k.", "Drunk." },
            { "D***kest", "Drunkest" },
            { "D***king", "Drinking" },
            { "D**g", "Drug" },
            { "Drunk It", "Drink It" },
            { "F***e", "Force" },
            { "G*******g", "Gangbang" },
            { "H*******m", "Hypnotism" },
            { "I****t", "Incest" },
            { "K**l", "Kill" },
            { "K*d", "Kid" },
            { "M****t", "Molest" },
            { "M************n", "Mother and Son" },
            { "P****h", "Punish" },
            { "R****g", "Raping" },
            { "R**e", "Rape" },
            { "Sai****", "Saitama" },
            { "S*********l", "Schoolgirl" },
            { "S********l", "Schoolgirl" },
            { "S*****t", "Student" },
            { "S***e", "Slave" },
            { "S******g", "Sleeping" },
            { "SK**led", "Skilled" },
            { "SK**lful", "Skillful" },
            { "SK**ls", "Skills" },
            { "T******e", "Tentacle" },
            { "T*****e", "Torture" },
            { "U*********s", "Unconscious" },
            { "V*****e", "Violate" },
            { "V*****t", "Violent" },
            { "V******e", "Violence" },
            { "Y********l's", "Young Girl's" },
        };

        private static readonly Dictionary<string, string[]> CrossSiteDb = new Dictionary<string, string[]>
        {
            { "DVAJ-", new[] { "DVAJ-0", "DVAJ-0003", "DVAJ-0013", "DVAJ-0021", "DVAJ-0031", "DVAJ-0039" } },
            { "DVAJ-0", new[] { "DVAJ-00", "DVAJ-0027", "DVAJ-0032" } },
            { "STAR-128_2008-11-06", new[] { "STAR-128" } },
            { "STAR-134_2008-12-18", new[] { "STAR-134" } },
        };

        private static readonly string[] IgnoreList = { "SEXY", "MEEL", "SKOT", "SCD", "GDSC" };
    }
}
