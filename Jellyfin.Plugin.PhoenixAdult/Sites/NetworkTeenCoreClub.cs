using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkTeenCoreClub : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out var id))
            {
                sceneId = id.ToString();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}videos/browse/search/{Uri.EscapeDataString(searchTitle)}?page=1&sg=false&sort=release&video_type=scene&lang=en&site_id=10&genre=0&dach=false";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var data = JObject.Parse(httpResult.Content);
            var videos = data.SelectToken("videos.data");
            if (videos != null && videos.Type != JTokenType.Null)
            {
                foreach (var searchResult in videos)
                {
                    string titleNoFormatting = searchResult["title"]["en"].ToString();
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(searchResult["publication_date"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string searchId = searchResult["id"].ToString();
                    string curId = Helper.Encode($"{Helper.GetSearchSearchURL(siteNum)}/videodetail/{searchId}");

                    var actors = searchResult["actors"].Select(a => a["name"].ToString());
                    string actorsList = string.Join(", ", actors);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} ({actorsList}) [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var data = JObject.Parse(httpResult.Content);
            var video = data["video"];

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = video["title"]["en"].ToString();
            movie.Overview = video["description"]["en"].ToString();
            movie.AddStudio("Teen Core Club");

            string tagline = video["labels"][0]["name"].ToString().Replace(".com", string.Empty).Trim();
            movie.AddStudio(tagline);

            var actors = new List<string>();
            foreach (var actorData in video["actors"])
            {
                string actorName = actorData["name"].ToString();
                actors.Add(actorName);
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            if (movie.Name.ToLower().StartsWith("bic_"))
            {
                if (actors.Count == 1)
                {
                    movie.Name = actors[0];
                }
                else if (actors.Count == 2)
                {
                    movie.Name = string.Join(" & ", actors);
                }
                else
                {
                    movie.Name = string.Join(", ", actors);
                }
            }

            if (DateTime.TryParse(video["publication_date"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreData in video["genres"])
            {
                var genre = genreData.SelectToken("title.en")?.ToString();
                if (!string.IsNullOrEmpty(genre))
                {
                    movie.AddGenre(genre);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var data = JObject.Parse(httpResult.Content);
            var video = data["video"];

            var artworkSmall = video.SelectToken("artwork.small")?.ToString();
            if (!string.IsNullOrEmpty(artworkSmall))
            {
                images.Add(new RemoteImageInfo { Url = artworkSmall });
            }

            var artworkLarge = video.SelectToken("artwork.large")?.ToString();
            if (!string.IsNullOrEmpty(artworkLarge))
            {
                images.Add(new RemoteImageInfo { Url = artworkLarge });
            }

            var coverSmall = video.SelectToken("cover.small")?.ToString();
            if (!string.IsNullOrEmpty(coverSmall))
            {
                images.Add(new RemoteImageInfo { Url = coverSmall });
            }

            var coverMedium = video.SelectToken("cover.medium")?.ToString();
            if (!string.IsNullOrEmpty(coverMedium))
            {
                images.Add(new RemoteImageInfo { Url = coverMedium });
            }

            var coverLarge = video.SelectToken("cover.large")?.ToString();
            if (!string.IsNullOrEmpty(coverLarge))
            {
                images.Add(new RemoteImageInfo { Url = coverLarge });
            }

            if (video["screenshots"] != null)
            {
                foreach (var screenshot in video["screenshots"])
                {
                    images.Add(new RemoteImageInfo { Url = screenshot.ToString() });
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
