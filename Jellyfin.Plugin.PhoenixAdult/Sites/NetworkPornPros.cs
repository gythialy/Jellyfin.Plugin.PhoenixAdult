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

            var directURL = searchTitle
                .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
                .Replace("'", "-", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(directURL.AsSpan(directURL.Length - 1, 1), out _) && directURL.Substring(directURL.Length - 2, 1) == "-")
            {
                directURL = $"{directURL.Substring(0, directURL.Length - 1)}-{directURL.Substring(directURL.Length - 1, 1)}";
            }

            var sceneURL = new Uri(Helper.GetSearchSearchURL(siteNum) + directURL);
            var sceneID = new List<string> { Helper.Encode(sceneURL.AbsolutePath) };

            if (searchDate.HasValue)
            {
                sceneID.Add(searchDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID.ToArray(), searchDate, cancellationToken).ConfigureAwait(false);
            if (searchResult.Any())
            {
                result.AddRange(searchResult);
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

                result.Item.PremiereDate = searchResults[0].PremiereDate;
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

                    result.People.Add(new PersonInfo
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
