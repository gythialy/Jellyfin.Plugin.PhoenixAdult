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
    public class NetworkKink : IProviderBase
    {
        private static readonly IDictionary<string, string> ChannelsList = new Dictionary<string, string>
        {
            { "30minutesoftorment", "30 Minutes of Torment" },
            { "boundgangbangs", "Bound Gangbangs" },
            { "boundgods", "Bound Gods" },
            { "boundinpublic", "Bound in Public" },
            { "brutalsessions", "Brutal Sessions" },
            { "buttmachineboys", "Butt Machine Boys" },
            { "captivemale", "Captive Male" },
            { "chantasbitches", "Chantas Bitches" },
            { "devicebondage", "Device Bondage" },
            { "divinebitches", "Divine Bitches" },
            { "electrosluts", "Electrosluts" },
            { "everythingbutt", "Everything Butt" },
            { "familiestied", "Families Tied" },
            { "fetishnetwork", "Fetish Network" },
            { "fetishnetworkmale", "Fetish Network Male" },
            { "filthyfemdom", "Filthy Femdom" },
            { "footworship", "Foot Worship" },
            { "fuckedandbound", "Fucked and Bound" },
            { "fuckingmachines", "Fucking Machines" },
            { "hardcoregangbang", "Hardcore Gangbang" },
            { "hogtied", "Hogtied" },
            { "kinkfeatures", "Kink Features" },
            { "kinkuniversity", "Kink University" },
            { "meninpain", "Men in Pain" },
            { "menonedge", "Men on Edge" },
            { "nakedkombat", "Naked Kombat" },
            { "publicdisgrace", "Public Disgrace" },
            { "sadisticrope", "Sadistic Rope" },
            { "sexandsubmission", "Sex and Submission" },
            { "sexualdisgrace", "Sexual Disgrace" },
            { "straponsquad", "Strapon Squad" },
            { "submissivex", "SubmissiveX" },
            { "thetrainingofo", "The Training of O" },
            { "theupperfloor", "The Upper Floor" },
            { "tspussyhunters", "TS Pussy Hunters" },
            { "tsseduction", "TS Seduction" },
            { "ultimatesurrender", "Ultimate Surrender" },
            { "waterbondage", "Water Bondage" },
            { "whippedass", "Whipped Ass" },
            { "wiredpussy", "Wired Pussy" },
        };

        private readonly IDictionary<string, string> cookies = new Dictionary<string, string>
        {
            { "viewing-preferences", "straight%2Cgay" },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var splitedTitle = searchTitle.Split()[0];
            if (int.TryParse(splitedTitle, out _))
            {
                var sceneURL = new Uri(Helper.GetSearchBaseURL(siteNum) + $"/shoot/{splitedTitle}");
                var sceneID = new string[] { Helper.Encode(sceneURL.AbsolutePath) };

                var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID, searchDate, cancellationToken).ConfigureAwait(false);
                if (searchResult.Any())
                {
                    result.AddRange(searchResult);
                }
            }
            else
            {
                var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle) + "&ageverified=g";

                // kink.com 被 Cloudflare 拦裸请求，需 FlareSolverr 浏览器上下文；无配置时直连兜底
                var data = await this.GetSearchPage(url, cancellationToken);
                if (data == null)
                {
                    return result;
                }

                // 新版页面: div.card.shoot-thumbnail, 链接 a[href^=/shoot/], 标题 a[title]
                var searchResults = data.SelectNodesSafe("//div[contains(concat(' ', normalize-space(@class), ' '), ' shoot-thumbnail ')]");
                foreach (var searchResult in searchResults)
                {
                    var linkNode = searchResult.SelectSingleNode(".//a[starts-with(@href, '/shoot/')]");
                    if (linkNode == null)
                    {
                        continue;
                    }

                    var href = linkNode.GetAttributeValue("href", string.Empty);
                    var sceneURL = new Uri(Helper.GetSearchBaseURL(siteNum) + href);
                    var titleNode = searchResult.SelectSingleNode(".//*[contains(@class, 'card-body-title')]//a");
                    string curID = Helper.Encode(sceneURL.AbsolutePath),
                        sceneName = titleNode?.GetAttributeValue("title", string.Empty) ?? string.Empty,
                        scenePoster = searchResult.SelectSingleText(".//img[contains(@src, 'imagedb')]/@src"),
                        sceneDate = string.Empty;

                    if (string.IsNullOrEmpty(sceneName))
                    {
                        sceneName = linkNode.GetAttributeValue("aria-label", string.Empty);
                    }

                    if (string.IsNullOrEmpty(scenePoster))
                    {
                        scenePoster = searchResult.SelectSingleText(".//img/@src");
                    }

                    var dateNode = searchResult.SelectSingleNode(".//small");
                    if (dateNode != null)
                    {
                        sceneDate = Regex.Match(dateNode.InnerText, @"[A-Z][a-z]{2}\s\d{1,2},\s\d{4}").Value;
                    }

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = sceneName,
                        ImageUrl = scenePoster,
                    };

                    if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParseExact(sceneDate, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                    {
                        res.PremiereDate = sceneDateObj;
                    }

                    result.Add(res);
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

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.cookies).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;

            result.Item.Name = sceneData.SelectSingleText("//h1[@class='shoot-title']/text()");
            result.Item.Overview = sceneData.SelectSingleText("//*[@class='description-text']");
            result.Item.AddStudio("Kink");
            var channel = sceneData.SelectSingleText("//div[contains(@class, 'shoot-logo')]//a/@href").Split("/").Last();
            if (!string.IsNullOrEmpty(channel))
            {
                if (ChannelsList.ContainsKey(channel))
                {
                    result.Item.AddStudio(ChannelsList[channel]);
                }
            }

            var sceneDate = sceneData.SelectSingleText("//span[@class='shoot-date']");
            if (DateTime.TryParseExact(sceneDate, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var genres = sceneData.SelectNodesSafe("//p[@class='tag-list category-tag-list']//a");
            foreach (var genreLink in genres)
            {
                var genreName = genreLink.InnerText;

                result.Item.AddGenre(genreName);
            }

            var actors = sceneData.SelectNodesSafe("//p[@class='starring']//a");
            foreach (var actorLink in actors)
            {
                string actorName = actorLink.InnerText.Replace(",", string.Empty, StringComparison.OrdinalIgnoreCase),
                    actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorLink.Attributes["href"].Value;

                var res = new PersonInfo
                {
                    Name = actorName,
                };

                var actorHTML = await HTML.ElementFromURL(actorPageURL, cancellationToken, null, this.cookies).ConfigureAwait(false);
                var actorPhoto = actorHTML.SelectSingleText("//div[contains(@class, 'biography-container')]//img/@src");

                if (!string.IsNullOrEmpty(actorPhoto))
                {
                    res.ImageUrl = actorPhoto;
                }

                result.AddPerson(res);
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

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.cookies).ConfigureAwait(false);

            var sceneImages = sceneData.SelectNodesSafe("//video");
            foreach (var sceneImage in sceneImages)
            {
                result.Add(new RemoteImageInfo
                {
                    Url = sceneImage.Attributes["poster"].Value,
                    Type = ImageType.Primary,
                });
            }

            sceneImages = sceneData.SelectNodesSafe("//div[@class='player']//img");
            foreach (var sceneImage in sceneImages)
            {
                result.Add(new RemoteImageInfo
                {
                    Url = sceneImage.Attributes["src"].Value,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = sceneImage.Attributes["src"].Value,
                    Type = ImageType.Backdrop,
                });
            }

            sceneImages = sceneData.SelectNodesSafe("//div[@id='gallerySlider']//img");
            foreach (var sceneImage in sceneImages)
            {
                result.Add(new RemoteImageInfo
                {
                    Url = sceneImage.Attributes["data-image-file"].Value,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = sceneImage.Attributes["data-image-file"].Value,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }

        private async Task<HtmlAgilityPack.HtmlNode> GetSearchPage(string url, CancellationToken cancellationToken)
        {
            if (PhoenixAdult.Helpers.Utils.FlareSolverr.IsConfigured)
            {
                var html = await PhoenixAdult.Helpers.Utils.FlareSolverr.GetHtml(url, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(html))
                {
                    return null;
                }

                return PhoenixAdult.Helpers.Utils.HTML.ElementFromString(html);
            }

            return await PhoenixAdult.Helpers.Utils.HTML.ElementFromURL(url, cancellationToken, null, this.cookies).ConfigureAwait(false);
        }
    }
}
