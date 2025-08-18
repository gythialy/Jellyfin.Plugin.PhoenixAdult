using System;
using System.Collections.Generic;
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
    public class NetworkPureCFNM : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string modelId;
            string sceneTitle;
            try
            {
                modelId = string.Join("-", searchTitle.Split(' ').Take(2));
                sceneTitle = string.Join(" ", searchTitle.Split(' ').Skip(2));
            }
            catch
            {
                modelId = searchTitle.Split(' ').First();
                sceneTitle = string.Join(" ", searchTitle.Split(' ').Skip(1));
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}{modelId}.html";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchResults.SelectNodes("//div[@class='update_block']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode(".//span[@class='update_title']")?.InnerText.Trim();
                    string description = node.SelectSingleNode(".//span[@class='latest_update_description']")?.InnerText.Trim();
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//span[@class='update_date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var actorList = node.SelectNodes(".//span[@class='tour_update_models']/a")?.Select(a => a.InnerText.Trim());
                    string actors = string.Join(", ", actorList ?? new string[0]);

                    string poster = node.SelectSingleNode(".//div[@class='update_image']/a/img")?.GetAttributeValue("src", string.Empty);
                    string subSite = Helper.GetSearchSiteName(siteNum);

                    string curId = Helper.Encode(titleNoFormatting);
                    string descriptionId = Helper.Encode(description);
                    string posterId = Helper.Encode(poster);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{descriptionId}|{releaseDate}|{actors}|{posterId}" } },
                        Name = $"{titleNoFormatting} [PureCFNM/{subSite}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            return result;
        }

        public Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string[] providerIds = sceneID[0].Split('|');
            string sceneTitle = Helper.Decode(providerIds[0]);
            string sceneDescription = Helper.Decode(providerIds[2]);
            string sceneDate = providerIds[3];
            string sceneActors = providerIds[4];

            var movie = (Movie)result.Item;
            movie.Name = sceneTitle;
            movie.Overview = sceneDescription;
            movie.AddStudio("PureCFNM");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            string subSite = Helper.GetSearchSiteName(siteNum).ToLower();
            if (subSite == "amateurcfnm") movie.AddGenre("CFNM");
            else if (subSite == "cfnmgames")
            {
                movie.AddGenre("CFNM");
                movie.AddGenre("Femdom");
            }
            else if (subSite == "girlsabuseguys")
            {
                movie.AddGenre("CFNM");
                movie.AddGenre("Femdom");
                movie.AddGenre("Male Humiliation");
            }
            else if (subSite == "heylittledick")
            {
                movie.AddGenre("CFNM");
                movie.AddGenre("Femdom");
                movie.AddGenre("Small Penis Humiliation");
            }
            else if (subSite == "ladyvoyeurs")
            {
                movie.AddGenre("CFNM");
                movie.AddGenre("Voyeur");
            }
            else if (subSite == "purecfnm")
            {
                movie.AddGenre("CFNM");
            }

            if (DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actors = sceneActors.Split(',');
            if (actors.Length > 0)
            {
                if (actors.Length == 2)
                {
                    movie.AddGenre("Threesome");
                }

                if (actors.Length == 3)
                {
                    movie.AddGenre("Foursome");
                }

                if (actors.Length > 3)
                {
                    movie.AddGenre("Group");
                }

                foreach (var actor in actors)
                {
                    result.People.Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
                }
            }

            return Task.FromResult(result);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string scenePoster = Helper.Decode(sceneID[0].Split('|')[5]);
            images.Add(new RemoteImageInfo { Url = scenePoster, Type = ImageType.Primary });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
