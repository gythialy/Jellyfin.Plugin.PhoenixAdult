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
    public class NetworkDirtyFlix : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> genresDB = new Dictionary<string, List<string>>
        {
            { "Trick Your GF", new List<string> { "Girlfriend", "Revenge" } },
            { "Make Him Cuckold", new List<string> { "Cuckold" } },
            { "She Is Nerdy", new List<string> { "Glasses", "Nerd" } },
            { "Tricky Agent", new List<string> { "Agent", "Casting" } },
        };

        private static readonly Dictionary<string[], string[]> xPathDB = new Dictionary<string[], string[]>
        {
            { new[] { "Trick Your GF", "Make Him Cuckold" }, new[] { ".//a[contains(@class, 'link')]", ".//div[@class='description']" } },
            { new[] { "She Is Nerdy" }, new[] { ".//a[contains(@class, 'title')]", ".//div[@class='description']" } },
            { new[] { "Tricky Agent" }, new[] { ".//h3", ".//div[@class='text']" } },
        };

        private static readonly Dictionary<string, Tuple<int, int>> siteDB = new Dictionary<string, Tuple<int, int>>
        {
            { "Trick Your GF", new Tuple<int, int>(7, 4) },
            { "Make Him Cuckold", new Tuple<int, int>(9, 5) },
            { "She Is Nerdy", new Tuple<int, int>(10, 12) },
            { "Tricky Agent", new Tuple<int, int>(11, 4) },
        };

        private static readonly Dictionary<string, List<string>> sceneActorsDB = new Dictionary<string, List<string>>
        {
            {"Adele Hotness", new List<string> {"snc162"}},
            {"Adina", new List<string> {"darygf050"}},
            {"Aggie", new List<string> {"wrygf726", "wtag728"}},
            {"Aimee Ryan", new List<string> {"pfc070"}},
            {"Akira Drago", new List<string> {"snc185"}},
            {"Alaina Dawson", new List<string> {"crygf009"}},
            {"Alex Swon", new List<string> {"snc141"}},
            {"Alexa Black", new List<string> {"snc139"}},
            {"Alexis Crash", new List<string> {"wrygf499"}},
            {"Alexis Crystal", new List<string> {"danc129"}},
            {"Alice Kelly", new List<string> {"snc143"}},
            {"Alice Lee", new List<string> {"wrygf776", "wtag770", "wnc765"}},
            {"Alice Marshall", new List<string> {"wrygf930", "wfc929", "wtag934", "wnc936"}},
            {"Alice Paradise", new List<string> {"wnc1665"}},
            {"Alien Fox", new List<string> {"wnc1625", "wnc1555"}},
            {"Alita Angel", new List<string> {"snc099"}},
            {"Amalia Davis", new List<string> {"wnc1560"}},
            {"Amanda Moore", new List<string> {"pfc111"}},
            {"Amber Daikiri", new List<string> {"wrygf508"}},
            {"Ami Calienta", new List<string> {"snc084"}},
            {"Ananta Shakti", new List<string> {"wtag893", "wnc810", "wnc888"}},
            {"Andrea Sun", new List<string> {"darygf057"}},
            {"Angel Dickens", new List<string> {"wfc391"}},
            {"Angel Piaff", new List<string> {"pfc119"}},
            {"Angela Vidal", new List<string> {"wnc1439"}},
            {"Angie Koks", new List<string> {"wrygf911"}},
            {"Angie Moon", new List<string> {"wtag1050"}},
            {"Ann Marie", new List<string> {"wfc715", "wtag719", "wnc714"}},
            {"Anna Krowe", new List<string> {"snc107"}},
            {"Anne Angel", new List<string> {"wtag577"}},
            {"Annette", new List<string> {"wtag579"}},
            {"Annika Seren", new List<string> {"wrygf451"}},
            {"April Storm", new List<string> {"wnc1455"}},
            {"Ariadna Moon", new List<string> {"wtag994"}},
            {"Ariana Shaine", new List<string> {"wnc1487"}},
            {"Aruna Aghora", new List<string> {"wrygf900", "wfc907", "wtag955", "wtag908"}},
            {"Ashley Woods", new List<string> {"dafc140"}},
            {"Aurora Sky", new List<string> {"snc129"}},
            {"Azumi Liu", new List<string> {"snc170"}},
            {"Baby Mamby", new List<string> {"snc199"}},
            {"Bell Knock", new List<string> {"snc073"}},
            {"Bella Gray", new List<string> {"wnc1668", "afnc004"}},
            {"Bella Mur", new List<string> {"wnc1517"}},
            {"Bernie Svintis", new List<string> {"irnc009"}},
            {"Black Angel", new List<string> {"wnc1597"}},
            {"Bloom Lambie", new List<string> {"snc147", "wnc1564"}},
            {"Carmen Fox", new List<string> {"wnc833", "wfc863", "wtag892"}},
            {"Carmen Rodriguez", new List<string> {"snc197"}},
            {"Cassidy Klein", new List<string> {"cfc005"}},
            {"Chanel Lux", new List<string> {"wnc1238"}},
            {"Chloe Blue", new List<string> {"wrygf526"}},
            {"Christi Cats", new List<string> {"wrygf553", "wtag584"}},
            {"Clockwork Victoria", new List<string> {"wnc1629", "snc120"}},
            {"Cornelia Quinn", new List<string> {"wnc1467"}},
            {"Darcy Dark", new List<string> {"wnc1590", "wnc1672"}},
            {"Dayana Kamil", new List<string> {"snc136"}},
            {"Denisa Peterson", new List<string> {"pfc056"}},
            {"Donna Keller", new List<string> {"wfc408"}},
            {"Dushenka", new List<string> {"wfc701", "wtag700", "wnc699"}},
            {"Elin Holm", new List<string> {"wnc1453"}},
            {"Elisaveta Gulobeva", new List<string> {"wfc926", "wtag922"}},
            {"Eliza Thorne", new List<string> {"snc117"}},
            {"Ellie", new List<string> {"pfc129"}},
            {"Emily C", new List<string> {"wnc1676", "snc203"}},
            {"Emily Thorne", new List<string> {"wtag1163"}},
            {"Emily Wilson", new List<string> {"snc114"}},
            {"Emma Brown", new List<string> {"wfc1089", "wtag1102"}},
            {"Emma Fantazy", new List<string> {"wnc1448"}},
            {"Eva", new List<string> {"wrygf865", "wnc880"}},
            {"Eva Red", new List<string> {"wnc1707"}},
            {"Eveline Neill", new List<string> {"pfc123"}},
            {"Evelyn Cage", new List<string> {"wrygf651", "wtag644"}},
            {"Foxy Di", new List<string> {"wrygf886", "wrygf828", "wtag859", "wnc821"}},
            {"Foxy Katrina", new List<string> {"wfc866", "wtag871", "wnc872"}},
            {"Francheska", new List<string> {"wfc406"}},
            {"Gina Gerson", new List<string> {"wrygf622", "wtag617", "wnc690"}},
            {"Gisha Forza", new List<string> {"wrygf1442", "snc087"}},
            {"Gloria Miller", new List<string> {"wrygf738", "wnc735"}},
            {"Glorie", new List<string> {"darygf052"}},
            {"Grace Young", new List<string> {"wfc380"}},
            {"Grace", new List<string> {"wfc960"}},
            {"Hanna Rey", new List<string> {"wnc1550", "wnc1622", "wnc1550"}},
            {"Hazel Dew", new List<string> {"wnc1301"}},
            {"Henna Ssy", new List<string> {"wnc1599"}},
            {"Herda Wisky", new List<string> {"wnc1294"}},
            {"Holly Molly", new List<string> {"snc184"}},
            {"Hungry Fox", new List<string> {"snc218"}},
            {"Inga Zolva", new List<string> {"wrygf747", "wtag767", "wnc879", "wnc746"}},
            {"Iris Kiss", new List<string> {"snc165", "wnc1637"}},
            {"Isabel Stern", new List<string> {"wfc1075"}},
            {"Iva Zan", new List<string> {"wrygf536", "wtag558", "wnc745"}},
            {"Izi Ashley", new List<string> {"wfc978", "wtag980", "wnc97", "wnc976"}},
            {"Jane Fox", new List<string> {"wtag1235"}},
            {"Jenny Fer", new List<string> {"wnc1330"}},
            {"Jenny Love", new List<string> {"wrygf634", "wfc607", "wtag601"}},
            {"Jenny Manson", new List<string> {"wtag1324", "wfc1302"}},
            {"Jessica Malone", new List<string> {"wrygf1078", "wtag1101", "wnc1086"}},
            {"Jessica Rox", new List<string> {"prygf138", "pfc137"}},
            {"Jessy Nikea", new List<string> {"wfc374"}},
            {"Jolie Butt", new List<string> {"wnc1703"}},
            {"Kari Sweet", new List<string> {"prygf135", "pfc134"}},
            {"Karry Slot", new List<string> {"snc080"}},
            {"Kate Quinn", new List<string> {"snc159"}},
            {"Katrin Tequila", new List<string> {"wnc1256"}},
            {"Katty Blessed", new List<string> {"wnc1176"}},
            {"Katty West", new List<string> {"wtag1181", "wnc1483"}},
            {"Katya", new List<string> {"wtag1181", "wnc758"}},
            {"Kelly Rouss", new List<string> {"snc091"}},
            {"Kendra Cole", new List<string> {"hrygf002"}},
            {"Kerry Cherry", new List<string> {"wnc1284"}},
            {"Kiara Gold", new List<string> {"wnc1545"}},
            {"Kiara Knight", new List<string> {"crygf002"}},
            {"Kimberly Mansell", new List<string> {"wrygf528"}},
            {"Kira Parvati", new List<string> {"wtag777", "wnc778"}},
            {"Kira Roller", new List<string> {"wnc1338"}},
            {"Kira Stone", new List<string> {"snc171"}},
            {"Kris the Foxx", new List<string> {"wnc1593", "wnc1506"}},
            {"Kylie Green", new List<string> {"wnc1614"}},
            {"Lagoon Blaze", new List<string> {"snc131"}},
            {"Lana Broks", new List<string> {"snc158"}},
            {"Lana Roy", new List<string> {"wnc1573"}},
            {"Lena Love", new List<string> {"wfc600", "wtag580"}},
            {"Li Loo", new List<string> {"wnc1536"}},
            {"Lia Chalizo", new List<string> {"wfc373"}},
            {"Lia Little", new List<string> {"snc133"}},
            {"Light Fairy", new List<string> {"wnc1608", "wnc1525"}},
            {"Lina Arian Joy", new List<string> {"wfc954"}},
            {"Lina Napoli", new List<string> {"wrygf760", "wtag772", "wnc752"}},
            {"Lindsey Olsen", new List<string> {"wfc589", "wtag598"}},
            {"Liona Levi", new List<string> {"wrygf663", "wtag660"}},
            {"Lita", new List<string> {"wfc902"}},
            {"Little Candy", new List<string> {"wnc1394"}},
            {"Liza Kolt", new List<string> {"wtag741", "wnc732"}},
            {"Lizaveta Kay", new List<string> {"wrygf733"}},
            {"Lizi Smoke", new List<string> {"snc122"}},
            {"Lola Shine", new List<string> {"wnc1173"}},
            {"Lorrelai Gold", new List<string> {"wnc816"}},
            {"Lottie Magne", new List<string> {"snc111"}},
            {"Luna Haze", new List<string> {"snc187"}},
            {"Luna Umberlay", new List<string> {"wnc1651"}},
            {"Madlen", new List<string> {"wnc1430"}},
            {"Maggie Gold", new List<string> {"pfc057"}},
            {"Margarita C", new List<string> {"wfc940", "wnc941"}},
            {"Margo Von Teese", new List<string> {"wnc1741"}},
            {"Maribel", new List<string> {"wfc367"}},
            {"Mary Solaris", new List<string> {"wnc1500"}},
            {"Matty", new List<string> {"irnc003"}},
            {"Mazy Teen", new List<string> {"pfc069"}},
            {"Megan Promesita", new List<string> {"pfc104"}},
            {"Megan Venturi", new List<string> {"wnc1539"}},
            {"Melissa Benz", new List<string> {"wfc1276"}},
            {"Meow Miu", new List<string> {"wnc1742"}},
            {"Mia Hilton", new List<string> {"pfc065"}},
            {"Mia Piper", new List<string> {"snc182", "snc240"}},
            {"Mia Reese", new List<string> {"wtag887", "wnc890"}},
            {"Michelle Can", new List<string> {"wfc1392", "wnc1354"}},
            {"Mila Gimnasterka", new List<string> {"wfc1100"}},
            {"Milana Milka", new List<string> {"wnc1639"}},
            {"Milena Briz", new List<string> {"snc195"}},
            {"Mileva", new List<string> {"irnc005"}},
            {"Milka Feer", new List<string> {"snc214"}},
            {"Mirta", new List<string> {"wfc919"}},
            {"Molly Brown", new List<string> {"snc088"}},
            {"Molly Manson", new List<string> {"crygf013"}},
            {"Monica Rise", new List<string> {"crygf011"}},
            {"Monika Jelolt", new List<string> {"achnc01"}},
            {"Monroe Fox", new List<string> {"wnc1671", "wnc1587"}},
            {"Nataly Gold", new List<string> {"wtag594"}},
            {"Natalya C", new List<string> {"wtag649"}},
            {"Nelya Smalls", new List<string> {"wnc1273"}},
            {"Nesti", new List<string> {"wfc696", "wnc697"}},
            {"Nika A", new List<string> {"snc223"}},
            {"Nika Charming", new List<string> {"wnc1513"}},
            {"Nikki Hill", new List<string> {"snc094"}},
            {"Norah Nova", new List<string> {"cfc008"}},
            {"Oliva Grace", new List<string> {"wrygf991", "wtag996"}},
            {"Olivia Cassi", new List<string> {"snc150"}},
            {"Petia", new List<string> {"pfc062"}},
            {"Petra Larkson", new List<string> {"pfc141"}},
            {"Pinky Breeze", new List<string> {"snc095"}},
            {"Queenlin", new List<string> {"snc155"}},
            {"Rahyndee James", new List<string> {"cfc003"}},
            {"Raquel Rimma", new List<string> {"wtag1134"}},
            {"Rebeca Fox", new List<string> {"snc194"}},
            {"Rebecca Rainbow", new List<string> {"wrygf1201", "wtag1193", "wnc1220"}},
            {"Regina Rich", new List<string> {"snc177", "wnc1635"}},
            {"Renata Fox", new List<string> {"wfc1215"}},
            {"Ria Koks", new List<string> {"wnc1377"}},
            {"Rin White", new List<string> {"wnc1679", "wnc1584"}},
            {"Rita Jalace", new List<string> {"wrygf633", "wtag643", "wnc740"}},
            {"Rita Lee", new List<string> {"wnc1369"}},
            {"Rita Milan", new List<string> {"wrygf870", "wfc968", "wtag961"}},
            {"Rita", new List<string> {"wtag1232"}},
            {"Rosa Mentoni", new List<string> {"wfc932"}},
            {"Roxy Lips", new List<string> {"wnc1458"}},
            {"Sabrina Moor", new List<string> {"wfc869", "wtag995"}},
            {"Salomja A", new List<string> {"wtag803"}},
            {"Sandra Wellness", new List<string> {"wnc1159"}},
            {"Sara Redz", new List<string> {"wtag804", "wnc800"}},
            {"Sara Rich", new List<string> {"wnc1647", "snc174"}},
            {"Selena Stuart", new List<string> {"wrygf798", "wnc801", "snc104"}},
            {"Sheeloves", new List<string> {"snc153"}},
            {"Sherry E", new List<string> {"wtag570"}},
            {"Shirley Harris", new List<string> {"wrygf485"}},
            {"Shrima Malati", new List<string> {"wrygf993", "wtag997"}},
            {"Sofi Goldfinger", new List<string> {"wtag1130"}},
            {"Sofy Soul", new List<string> {"wtag1252"}},
            {"Soni", new List<string> {"wrygf648", "wtag610"}},
            {"Sonya Sweet", new List<string> {"wfc1198", "wtag1204", "wnc1196"}},
            {"Stacy Snake", new List<string> {"wrygf427"}},
            {"Stasia Si", new List<string> {"wnc1609", "wnc1533"}},
            {"Stefanie Moon", new List<string> {"wfc1107", "wtag1122"}},
            {"Stefany Kyler", new List<string> {"wnc1569"}},
            {"Stella Flex", new List<string> {"wfc1431"}},
            {"Sunny Alika", new List<string> {"wfc1123"}},
            {"Sunny Rise", new List<string> {"wfc403"}},
            {"Sweet Cat", new List<string> {"wfc403", "danc115"}},
            {"Tais Afinskaja", new List<string> {"wfc497"}},
            {"Taissia Shanti", new List<string> {"wfc953", "wtag955"}},
            {"Taniella", new List<string> {"wtag675"}},
            {"Tarja King", new List<string> {"danc125"}},
            {"Timea Bella", new List<string> {"danc124"}},
            {"Tonya Nats", new List<string> {"wfc539"}},
            {"Vasilisa Lisa", new List<string> {"wnc1633"}},
            {"Veronika Fare", new List<string> {"wnc1315"}},
            {"Vika Lita", new List<string> {"wfc1480"}},
            {"Vika Volkova", new List<string> {"wtag1036"}},
            {"Viola Weber", new List<string> {"snc189"}},
            {"Violette Pink", new List<string> {"danc111"}},
            {"Vivian Grace", new List<string> {"wnc1654"}},
            {"Zena Little", new List<string> {"dafc139", "danc110"}},
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var (siteKey, sitePages) = siteDB[Helper.GetSearchSiteName(siteNum)];

            var tourHttpResult1 = await HTTP.Request($"http://dirtyflix.com/index.php/main/show_one_tour/{siteKey}", HttpMethod.Get, cancellationToken);
            var tourPageElements1 = HTML.ElementFromString(tourHttpResult1.Content);
            var tourHttpResult2 = await HTTP.Request($"http://dirtyflix.com/index.php/main/show_one_tour/{siteKey}/2", HttpMethod.Get, cancellationToken);
            var tourPageElements2 = HTML.ElementFromString(tourHttpResult2.Content);

            var scenes = sceneActorsDB.Where(kvp => kvp.Key.ToLower().Contains(searchTitle.ToLower())).SelectMany(kvp => kvp.Value).ToList();

            for (int i = 2; i < sitePages; i++)
            {
                var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{i}";
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchPageElements = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchPageElements.SelectNodes("//div[@class='movie-block']");
                    if (searchNodes != null)
                    {
                        foreach (var searchResult in searchNodes)
                        {
                            var xPath = xPathDB.FirstOrDefault(kvp => kvp.Key.Contains(Helper.GetSearchSiteName(siteNum))).Value;
                            string titleNoFormatting = Helper.ParseTitle(searchResult.SelectSingleNode(xPath[0])?.InnerText.Trim(), siteNum);

                            var match = Regex.Match(searchResult.SelectSingleNode(".//li/img")?.GetAttributeValue("src", string.Empty) ?? string.Empty, @"(?<=tour_thumbs/).*(?=/)");
                            if (match.Success)
                            {
                                string sceneId = match.Groups[0].Value;
                                string curId = Helper.Encode(sceneId);

                                string releaseDate = string.Empty;
                                var tourPageElements = tourPageElements1.SelectSingleNode($"//div[@class='thumbs-item'][.//*[contains(@src, \"{sceneId}\")]]");
                                if (tourPageElements != null)
                                {
                                    releaseDate = DateTime.Parse(tourPageElements.SelectSingleNode(".//span[@class='added']")?.InnerText.Trim()).ToString("yyyy-MM-dd");
                                }
                                else
                                {
                                    tourPageElements = tourPageElements2.SelectSingleNode($"//div[@class='thumbs-item'][.//*[contains(@src, \"{sceneId}\")]]");
                                    if (tourPageElements != null)
                                    {
                                        releaseDate = DateTime.Parse(tourPageElements.SelectSingleNode(".//span[@class='added']")?.InnerText.Trim()).ToString("yyyy-MM-dd");
                                    }
                                }

                                result.Add(new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}|{Helper.Encode(searchUrl)}" } },
                                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                                    SearchProviderName = Plugin.Instance.Name,
                                });
                            }
                        }
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
            string sceneId = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;
            string searchPageUrl = providerIds.Length > 3 ? Helper.Decode(providerIds[3]) : null;
            var (siteKey, sitePages) = siteDB[Helper.GetSearchSiteName(siteNum)];

            var httpResult = await HTTP.Request(searchPageUrl, HttpMethod.Get, cancellationToken);
            HtmlNode detailsPageElements = null;
            if (httpResult.IsOK)
            {
                detailsPageElements = HTML.ElementFromString(httpResult.Content)
                    .SelectSingleNode($"//div[@class='movie-block'][.//*[contains(@src, \"{sceneId}\")]]");
            }
            if (detailsPageElements == null)
            {
                int startPage = 2;
                if (int.TryParse(searchPageUrl.Split('/').Last(), out var page))
                {
                    startPage = page + 1;
                }
                for (int i = startPage; i < sitePages; i++)
                {
                    var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{i}";
                    httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var searchPageElements = HTML.ElementFromString(httpResult.Content);
                        var searchNodes = searchPageElements.SelectNodes("//div[@class='movie-block']");
                        if (searchNodes != null)
                        {
                            foreach (var searchResult in searchNodes)
                            {
                                var match = Regex.Match(searchResult.SelectSingleNode(".//li/img")?.GetAttributeValue("src", string.Empty) ?? string.Empty, @"(?<=tour_thumbs/).*(?=/)");
                                if (match.Success && match.Groups[0].Value == sceneId)
                                {
                                    detailsPageElements = searchResult;
                                    break;
                                }
                            }
                        }
                    }
                    if (detailsPageElements != null)
                    {
                        break;
                    }
                }
            }
            if (detailsPageElements == null)
            {
                return result;
            }

            var xPath = xPathDB.FirstOrDefault(kvp => kvp.Key.Contains(Helper.GetSearchSiteName(siteNum))).Value;

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode(xPath[0])?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode(xPath[1])?.InnerText.Trim();
            movie.AddStudio("Dirty Flix");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            if (genresDB.ContainsKey(tagline))
            {
                foreach (var genre in genresDB[tagline])
                {
                    movie.AddGenre(genre);
                }
            }

            var actors = sceneActorsDB.Where(kvp => kvp.Value.Contains(sceneId)).Select(kvp => kvp.Key);
            foreach (var actor in actors)
            {
                result.People.Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneId = Helper.Decode(providerIds[0]);
            string searchPageUrl = providerIds.Length > 3 ? Helper.Decode(providerIds[3]) : null;

            var httpResult = await HTTP.Request(searchPageUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = (HTML.ElementFromString(httpResult.Content))
                .SelectSingleNode($"//div[@class='movie-block'][.//*[contains(@src, \"{sceneId}\")]]");
            if (detailsPageElements == null)
            {
                return images;
            }

            var imageNode = detailsPageElements.SelectSingleNode(".//img");
            if (imageNode != null)
            {
                images.Add(new RemoteImageInfo { Url = imageNode.GetAttributeValue("src", string.Empty) });
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
