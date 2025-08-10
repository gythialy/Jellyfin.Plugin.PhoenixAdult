using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
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
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
                return result;

            string shootID = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                shootID = parts[0];
                searchTitle = searchTitle.Substring(shootID.Length).Trim();
            }

            if (!string.IsNullOrEmpty(shootID))
            {
                string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/shoot/{shootID}";
                var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Cookie", "viewing-preferences=straight%2Cgay" } });
                if (detailsPageElements != null)
                {
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'fs-0')]")?.InnerText.Trim();
                    string releaseDate = DateTime.Parse(detailsPageElements.SelectSingleNode("//div[contains(@class, 'shoot-detail-legend')]//span[@class='text-muted ms-2']")?.InnerText).ToString("yyyy-MM-dd");
                    string curID = Helper.Encode(sceneURL);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"[{shootID}] {titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        Score = 100,
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
            }
            else
            {
                var searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
                var searchResultsNode = await HTML.ElementFromURL(searchUrl, cancellationToken);
                if (searchResultsNode == null) return result;

                var searchResults = searchResultsNode.SelectNodes("//div[@class='shoot-card scene']");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        string titleNoFormatting = searchResult.SelectSingleNode(".//img")?.GetAttributeValue("alt", "").Trim();
                        string curID = Helper.Encode(searchResult.SelectSingleNode(".//a[@class='shoot-link']")?.GetAttributeValue("href", ""));
                        shootID = searchResult.SelectSingleNode(".//div[contains(@class, 'favorite-button')]")?.GetAttributeValue("data-id", "");

                        string releaseDateStr = string.Empty;
                        var dateNode = searchResult.SelectSingleNode(".//div[@class='date']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var releaseDate))
                            releaseDateStr = releaseDate.ToString("yyyy-MM-dd");

                        int score = searchDate.HasValue && !string.IsNullOrEmpty(releaseDateStr)
                            ? 100 - LevenshteinDistance.Compute(searchDate.Value.ToString("yyyy-MM-dd"), releaseDateStr)
                            : 100 - LevenshteinDistance.Compute(searchTitle.ToLower(), titleNoFormatting.ToLower());

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDateStr}" } },
                            Name = $"[{shootID}] {titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDateStr}",
                            Score = score,
                            SearchProviderName = Plugin.Instance.Name
                        });
                    }
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
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            foreach (var br in detailsPageElements.SelectNodes("//br"))
                br.ParentNode.ReplaceChild(HtmlNode.CreateNode("\n"), br);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'fs-0')]")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(@class, 'description')]/span[contains(@class, 'fw-200')]")?.InnerText.Replace('\n', ' ').Trim();

            string channel = detailsPageElements.SelectSingleNode("//div[contains(@class, 'shoot-detail-legend')]//a[contains(@href, '/channel/')]")?.InnerText.Trim().ToLower();
            string tagline = GetTagline(channel) ?? Helper.GetSearchSiteName(siteNum);
            movie.Tags.Add(tagline);
            movie.AddStudio(GetStudio(tagline));

            var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'shoot-detail-legend')]//span[@class='text-muted ms-2']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var releaseDate))
            {
                movie.PremiereDate = releaseDate;
                movie.ProductionYear = releaseDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedSceneDate))
            {
                movie.PremiereDate = parsedSceneDate;
                movie.ProductionYear = parsedSceneDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[contains(@href, '/tag/')]");
            if (genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Replace(",", "").Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//span[contains(@class, 'text-primary')]//a[contains(@href, '/model/')]");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3) movie.AddGenre("Threesome");
                if (actorNodes.Count == 4) movie.AddGenre("Foursome");
                if (actorNodes.Count > 4) movie.AddGenre("Orgy");

                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Replace(",", "").Trim();
                    string actorPageURL = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", "");
                    var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                    string actorPhotoURL = actorPage?.SelectSingleNode("//div[contains(@class, 'biography-container')]//img")?.GetAttributeValue("src", "");
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonType.Actor, ImageUrl = actorPhotoURL });
                }
            }

            var directorNodes = detailsPageElements.SelectNodes("//span[contains(@class, 'director-name')]/a");
            if(directorNodes != null)
            {
                foreach(var director in directorNodes)
                {
                    string directorName = director.InnerText.Trim();
                    string directorPageURL = Helper.GetSearchBaseURL(siteNum) + director.GetAttributeValue("href", "");
                    var directorPage = await HTML.ElementFromURL(directorPageURL, cancellationToken);
                    string directorPhotoURL = directorPage?.SelectSingleNode("//div[contains(@class, 'biography-container')]//img")?.GetAttributeValue("src", "");
                    result.People.Add(new PersonInfo { Name = directorName, Type = PersonType.Director, ImageUrl = directorPhotoURL });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return images;

            var xpaths = new[] { "//video/@poster", "//div[@class='player']/div/@poster", "//div[@id='galleryWrapper']//img/@data-image-file" };
            foreach(var xpath in xpaths)
            {
                var posterNodes = detailsPageElements.SelectNodes(xpath);
                if(posterNodes != null)
                {
                    foreach(var poster in posterNodes)
                        images.Add(new RemoteImageInfo { Url = poster.GetAttributeValue(xpath.Split('@').Last(), "").Split('?')[0] });
                }
            }
            return images;
        }

        private string GetTagline(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return null;
            var taglines = new Dictionary<string, string>
            {
                { "boundgangbangs", "Bound Gangbangs" }, { "brutalsessions", "Brutal Sessions" }, { "devicebondage", "Device Bondage" },
                { "familiestied", "Families Tied" }, { "hardcoregangbang", "Hardcore Gangbang" }, { "hogtied", "Hogtied" },
                { "kinkfeatures", "Kink Features" }, { "kinkuniversity", "Kink University" }, { "publicdisgrace", "Public Disgrace" },
                { "sadisticrope", "Sadistic Rope" }, { "sexandsubmission", "Sex and Submission" }, { "thetrainingofo", "The Training of O" },
                { "theupperfloor", "The Upper Floor" }, { "waterbondage", "Water Bondage" }, { "everythingbutt", "Everything Butt" },
                { "footworship", "Foot Worship" }, { "fuckingmachines", "Fucking Machines" }, { "tspussyhunters", "TS Pussy Hunters" },
                { "tsseduction", "TS Seduction" }, { "ultimatesurrender", "Ultimate Surrender" }, { "30minutesoftorment", "30 Minutes of Torment" },
                { "boundgods", "Bound Gods" }, { "boundinpublic", "Bound in Public" }, { "buttmachineboys", "Butt Machine Boys" },
                { "menonedge", "Men on Edge" }, { "nakedkombat", "Naked Kombat" }, { "divinebitches", "Divine Bitches" },
                { "electrosluts", "Electrosluts" }, { "meninpain", "Men in Pain" }, { "whippedass", "Whipped Ass" },
                { "wiredpussy", "Wired Pussy" }, { "chantasbitches", "Chantas Bitches" }, { "fuckedandbound", "Fucked and Bound" },
                { "captivemale", "Captive Male" }, { "submissivex", "SubmissiveX" }, { "filthyfemdom", "Filthy Femdom" },
                { "straponsquad", "Strapon Squad" }, { "sexualdisgrace", "Sexual Disgrace" }, { "fetishnetwork", "Fetish Network" },
                { "fetishnetworkmale", "Fetish Network Male" }
            };
            foreach(var pair in taglines)
            {
                if (channel.Contains(pair.Key))
                    return pair.Value;
            }
            return null;
        }

        private string GetStudio(string tagline)
        {
            if (tagline == "Chantas Bitches" || tagline == "Fucked and Bound" || tagline == "Captive Male")
                return "Twisted Factory";
            if (tagline == "Sexual Disgrace" || tagline == "Strapon Squad" || tagline == "Fetish Network Male" || tagline == "Fetish Network")
                return "Fetish Network";
            return "Kink";
        }
    }
}
