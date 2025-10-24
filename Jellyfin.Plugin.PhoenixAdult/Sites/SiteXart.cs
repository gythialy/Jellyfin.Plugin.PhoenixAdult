using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
    public class SiteXart : IProviderBase
    {
        private static readonly Dictionary<string, Dictionary<string, string>> manualMatch = new Dictionary<string, Dictionary<string, string>>
        {
            { "Out of This World", new Dictionary<string, string> { { "curID", "/videos/Out_of_This_World" }, { "name", "Out Of This World [X-Art]" } } },
            { "Beauteliful Girl", new Dictionary<string, string> { { "curID", "/videos/beauteliful_girl" }, { "name", "Beauteliful Girl [X-Art]" } } },
            { "Sunset", new Dictionary<string, string> { { "curID", "/videos/sunset" }, { "name", "Sunset [X-Art]" } } },
            { "Cum Like Crazy", new Dictionary<string, string> { { "curID", "/videos/cum_like_crazy" }, { "name", "Cum Like Crazy [X-Art]" } } },
            { "Tenderness", new Dictionary<string, string> { { "curID", "/videos/tenderness" }, { "name", "Tenderness [X-Art]" } } },
            { "My Love", new Dictionary<string, string> { { "curID", "/videos/my_love" }, { "name", "My Love [X-Art]" } } },
            { "Dream Girl", new Dictionary<string, string> { { "curID", "/videos/dream_girl" }, { "name", "Dream Girl [X-Art]" } } },
            { "Mutual Orgasm", new Dictionary<string, string> { { "curID", "/videos/mutual_orgasm" }, { "name", "Mutual Orgasm [X-Art]" } } },
            { "Delicious", new Dictionary<string, string> { { "curID", "/videos/delicious" }, { "name", "Delicious [X-Art]" } } },
            { "Girlfriends", new Dictionary<string, string> { { "curID", "/videos/girlfriends" }, { "name", "Girlfriends [X-Art]" } } },
            { "Just for You", new Dictionary<string, string> { { "curID", "/videos/just_for_you" }, { "name", "Just for You [X-Art]" } } },
            { "True Love", new Dictionary<string, string> { { "curID", "/videos/true_love" }, { "name", "True Love [X-Art]" } } },
            { "Intimate", new Dictionary<string, string> { { "curID", "/videos/intimate" }, { "name", "Intimate [X-Art]" } } },
            { "In Bed", new Dictionary<string, string> { { "curID", "/videos/in_bed" }, { "name", "In Bed [X-Art]" } } },
            { "Angelic", new Dictionary<string, string> { { "curID", "/videos/angelic" }, { "name", "Angelic [X-Art]" } } },
            { "Her First Time", new Dictionary<string, string> { { "curID", "/videos/her_first_time" }, { "name", "Her First Time [X-Art]" } } },
            { "Want You", new Dictionary<string, string> { { "curID", "/videos/want_you" }, { "name", "Want You [X-Art]" } } },
            { "Awakening", new Dictionary<string, string> { { "curID", "/videos/Awakening" }, { "name", "Awakening [X-Art]" } } },
            { "Rendezvous", new Dictionary<string, string> { { "curID", "/videos/Rendezvous" }, { "name", "Rendezvous [X-Art]" } } },
            { "Watching", new Dictionary<string, string> { { "curID", "/videos/watching" }, { "name", "Watching [X-Art]" } } },
            { "First Time", new Dictionary<string, string> { { "curID", "/videos/first_time" }, { "name", "First Time [X-Art]" } } },
            { "Sapphically Sexy Fucking Lesbians", new Dictionary<string, string> { { "curID", "/videos/sapphically_sexy_(fucking_lesbians)" }, { "name", "Sapphically Sexy (Fucking Lesbians) [X-Art]" } } },
            { "Je Mappelle Belle", new Dictionary<string, string> { { "curID", "/videos/je_m_appelle_belle" }, { "name", "Je M'Appelle Belle [X-Art]" } } },
            { "Sparks", new Dictionary<string, string> { { "curID", "/videos/sparks" }, { "name", "Sparks [X-Art]" } } },
            { "Hot Orgasm", new Dictionary<string, string> { { "curID", "/videos/hot_orgasm" }, { "name", "Hot Orgasm [X-Art]" } } },
            { "Group Sex", new Dictionary<string, string> { { "curID", "/videos/group_sex" }, { "name", "Group Sex [X-Art]" } } },
            { "A Cloudy Hot Day Milas First Lesbian Experience", new Dictionary<string, string> { { "curID", "/videos/a_cloudy_hot_day_(mila's_first_lesbian_experience)" }, { "name", "A Cloudy Hot Day (Mila's First Lesbian Experience) [X-Art]" } } },
            { "Our Little Cum Cottage", new Dictionary<string, string> { { "curID", "/videos/our_little_(cum)_cottage" }, { "name", "Our Little (Cum) Cottage [X-Art]" } } },
            { "Kacey Jordan Does X Art", new Dictionary<string, string> { { "curID", "/videos/kacey_jordan_does_x-art" }, { "name", "Kacey Jordan Does X-Art [X-Art]" } } },
            { "X Art on TV", new Dictionary<string, string> { { "curID", "/videos/x-art_on_tv" }, { "name", "X-Art on TV [X-Art]" } } },
            { "Lilys First Time Lesbian Loving", new Dictionary<string, string> { { "curID", "/videos/lilys_firsttime_lesbian_loving" }, { "name", "Lily's First-time Lesbian Loving [X-Art]" } } },
            { "I Love X Art", new Dictionary<string, string> { { "curID", "/videos/i_love_x-art" }, { "name", "I Love X-Art [X-Art]" } } },
            { "Don't Keep Me Waiting Part 1", new Dictionary<string, string> { { "curID", "/videos/dont_keep_me_waiting__part_1" }, { "name", "Don't Keep Me Waiting - Part 1 [X-Art]" } } },
            { "Don't Keep Me Waiting Part 2", new Dictionary<string, string> { { "curID", "/videos/dont_keep_me_waiting__part_2" }, { "name", "Don't Keep Me Waiting - Part 2 [X-Art]" } } },
            { "Luminated Sexual Emotions", new Dictionary<string, string> { { "curID", "/videos/luminated_(sexual)_emotions" }, { "name", "Luminated (Sexual) Emotions [X-Art]" } } },
            { "4 Way in 4k", new Dictionary<string, string> { { "curID", "/videos/4way_in_4k" }, { "name", "4-Way in 4K [X-Art]" } } },
            { "Cut Once More Please", new Dictionary<string, string> { { "curID", "/videos/cut!_once_more_please!" }, { "name", "Cut! Once More Please! [X-Art]" } } },
            { "Fine Finger Fucking", new Dictionary<string, string> { { "curID", "/videos/fine_fingerfucking" }, { "name", "Fine Finger-Fucking [X-Art]" } } },
            { "Skin Tillating Sex for Three", new Dictionary<string, string> { { "curID", "/videos/skintillating_sex_for_three" }, { "name", "Skin-Tillating Sex For Three [X-Art]" } } },
            { "Angelica Hotter Than Ever", new Dictionary<string, string> { { "curID", "/videos/angelicahotter_than_ever" }, { "name", "Angelica~Hotter Than Ever [X-Art]" } } },
            { "Domination Part 1", new Dictionary<string, string> { { "curID", "/videos/domination__part_1" }, { "name", "Domination - Part 1 [X-Art]" } } },
            { "The Rich Girl Part One", new Dictionary<string, string> { { "curID", "/videos/the_rich_girl_-_part_one" }, { "name", "The Rich Girl - Part One [X-Art]" } } },
            { "The Rich Girl Part Two", new Dictionary<string, string> { { "curID", "/videos/the_rich_girl_-_part_two" }, { "name", "The Rich Girl - Part Two [X-Art]" } } },
            { "Black & White", new Dictionary<string, string> { { "curID", "/videos/black_&_white" }, { "name", "Black & White [X-Art]" } } },
            { "Fashion Models", new Dictionary<string, string> { { "curID", "/videos/fashion_models" }, { "name", "Fashion Models [X-Art]" } } },
            { "Francesca Angelic", new Dictionary<string, string> { { "curID", "/videos/angelic" }, { "name", "Francesca Angelic [X-Art]" } } },
            { "Green Eyes", new Dictionary<string, string> { { "curID", "/videos/green_eyes" }, { "name", "Green Eyes [X-Art]" } } },
            { "Heart & Soul", new Dictionary<string, string> { { "curID", "/videos/heart_&_soul" }, { "name", "Heart & Soul [X-Art]" } } },
            { "La Love", new Dictionary<string, string> { { "curID", "/videos/l.a._love" }, { "name", "L.A. Love [X-Art]" } } },
            { "Naughty & Nice", new Dictionary<string, string> { { "curID", "/videos/naughty_&_nice" }, { "name", "Naughty & Nice [X-Art]" } } },
            { "One & Only Caprice", new Dictionary<string, string> { { "curID", "/videos/one_&_only_caprice" }, { "name", "One & Only Caprice [X-Art]" } } },
            { "Young & Hot", new Dictionary<string, string> { { "curID", "/videos/young_&_hot" }, { "name", "Young & Hot [X-Art]" } } },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchUrl = Helper.GetSearchSearchURL(siteNum);
            var values = new Dictionary<string, string>
            {
                { "input_search_sm", searchTitle }
            };
            var httpContent = new FormUrlEncodedContent(values);
            var http = await HTTP.Request(searchUrl, HttpMethod.Post, httpContent, cancellationToken);

            if (http.IsOK)
            {
                var searchResults = new HtmlDocument();
                searchResults.LoadHtml(http.Content);

                foreach (var searchResult in searchResults.DocumentNode.SelectNodes("//a[contains(@href, 'videos')]"))
                {
                    var linkNode = searchResult.SelectSingleNode(".//img[contains(@src, 'videos')]");
                    if (linkNode != null)
                    {
                        var titleNoFormatting = linkNode.GetAttributeValue("alt", "").Trim();
                        var releaseDateNode = searchResult.SelectSingleNode(".//h2[2]");
                        if (releaseDateNode != null && DateTime.TryParse(releaseDateNode.InnerText.Trim(), out var releaseDate))
                        {
                            var curID = Helper.Encode(searchResult.GetAttributeValue("href", ""));

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate:yyyy-MM-dd}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
                }
            }

            if (manualMatch.TryGetValue(searchTitle, out var manual))
            {
                var curID = Helper.Encode(manual["curID"]);
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = manual["name"],
                });
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
                HasMetadata = true
            };
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='row info']//div[contains(@class, 'columns')]")?.InnerText.Trim();
            var summaryNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'info')]//p");
            if (summaryNodes != null)
            {
                movie.Overview = string.Join("\n\n", summaryNodes.Select(p => p.InnerText.Trim()));
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectNodes("//h2")?.Skip(2).FirstOrDefault();
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Trim();
                if (dateText.EndsWith(","))
                {
                    dateText = dateText.TrimEnd(',');
                }

                if (DateTime.TryParseExact(dateText, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            movie.AddGenre("Artistic");
            movie.AddGenre("Glamorous");

            var actorNodes = doc.DocumentNode.SelectNodes("//h2//a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3) movie.AddGenre("Threesome");
                if (actorNodes.Count == 4) movie.AddGenre("Foursome");
                if (actorNodes.Count > 4) movie.AddGenre("Orgy");

                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorUrl = actorLink.GetAttributeValue("href", "");
                    if (!actorUrl.StartsWith("http"))
                    {
                        actorUrl = Helper.GetSearchBaseURL(siteNum) + actorUrl;
                    }

                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    var actorPhotoUrl = "";
                    if (actorHttp.IsOK)
                    {
                        var actorDoc = new HtmlDocument();
                        actorDoc.LoadHtml(actorHttp.Content);
                        actorPhotoUrl = actorDoc.DocumentNode.SelectSingleNode("//img[@class='info-img']")?.GetAttributeValue("src", "");
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var docs = new List<HtmlDocument>();
            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                docs.Add(doc);
            }

            var galleryURL = sceneURL.Replace("/videos/", "/galleries/");
            http = await HTTP.Request(galleryURL, HttpMethod.Get, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                docs.Insert(0, doc);
            }

            var xpaths = new[]
            {
                "//img[@alt='thumb']/@src",
                "//div[contains(@class, 'video-tour')]//a/img/@src",
                "//div[@class='gallery-item']//img/@src",
            };

            foreach (var doc in docs)
            {
                foreach (var xpath in xpaths)
                {
                    var nodes = doc.DocumentNode.SelectNodes(xpath);
                    if (nodes != null)
                    {
                        foreach (var poster in nodes)
                        {
                            var posterUrl = poster.GetAttributeValue("src", "");
                            if (posterUrl.Contains("videos"))
                            {
                                images.Add(new RemoteImageInfo { Url = posterUrl });
                                if (posterUrl.EndsWith("_1.jpg"))
                                    images.Add(new RemoteImageInfo { Url = posterUrl.Replace("_1.jpg", "_2.jpg") });
                                else if (posterUrl.EndsWith("_1-lrg.jpg"))
                                    images.Add(new RemoteImageInfo { Url = posterUrl.Replace("_1-lrg.jpg", "_2-lrg.jpg") });
                            }
                        }
                    }
                }
            }

            return images;
        }
    }
}
