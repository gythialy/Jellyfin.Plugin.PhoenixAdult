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
    public class NetworkRadicalCash : IProviderBase
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

            string searchUrl = $"{Helper.GetSearchBaseURL(siteNum)}/api/search/{Uri.EscapeDataString(searchTitle.ToLower())}";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = JObject.Parse(httpResult.Content);
            var data = siteNum[1] == 13 ? searchResults["playlists"] : searchResults["scenes"];
            if (data != null)
            {
                foreach (var searchResult in data)
                {
                    if (siteNum[1] == 32)
                    {
                        string titleNoFormatting = $"BTS: {Helper.ParseTitle(searchResult["title"].ToString(), siteNum)}";
                        string releaseDate = string.Empty;
                        if (DateTime.TryParse(searchResult["publish_date"].ToString(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/bts/{searchResult["slug"]}-bts";
                        string curId = Helper.Encode(sceneUrl);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curId } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                    else
                    {
                        string titleNoFormatting = searchResult["title"].ToString();
                        string videoId = searchResult["id"].ToString();
                        string releaseDate = string.Empty;
                        string sceneUrl;
                        if (siteNum[1] == 13)
                        {
                            if (DateTime.TryParse(searchResult["created_at"].ToString(), out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }

                            sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}/{videoId}/{searchResult["slug"]}";
                        }
                        else
                        {
                            if (DateTime.TryParse(searchResult["publish_date"].ToString(), out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }

                            sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}/{searchResult["slug"]}";
                        }

                        string curId = Helper.Encode(sceneUrl);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curId } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var videoPageElements = JObject.Parse((HTML.ElementFromString(httpResult.Content)).SelectSingleNode("//script[@type='application/json']")?.InnerText);
            var video = siteNum[1] == 13 ? videoPageElements["props"]["pageProps"]["playlist"] : videoPageElements["props"]["pageProps"]["content"];
            var content = siteNum[1] == 13 ? videoPageElements["props"]["pageProps"]["content"] : video;

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = Helper.ParseTitle(video["title"].ToString(), siteNum);
            movie.Overview = video["description"].ToString();
            if (!movie.Overview.EndsWith("."))
            {
                movie.Overview += ".";
            }

            if (siteNum[1] >= 3 && siteNum[1] <= 10)
            {
                movie.AddStudio("Top Web Models");
            }
            else if (siteNum[1] >= 0 && siteNum[1] <= 2)
            {
                movie.AddStudio("TwoWebMedia");
            }
            else
            {
                movie.AddStudio("Radical Cash");
            }

            string tagline = (siteNum[1] == 13 ? content["site"] : video["site"]).ToString();
            if (!movie.Studios.Contains(tagline))
            {
                movie.AddStudio(tagline);
            }

            if (DateTime.TryParse(video["publish_date"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genre in content["tags"])
            {
                movie.AddGenre(genre.ToString().Trim());
            }

            foreach (var actor in video["models_thumbs"])
            {
                string actorName = actor["name"].ToString();
                string actorPhotoUrl = actor["thumb"].ToString();
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
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

            var videoPageElements = JObject.Parse((HTML.ElementFromString(httpResult.Content)).SelectSingleNode("//script[@type='application/json']")?.InnerText);
            var content = siteNum[1] == 13 ? videoPageElements["props"]["pageProps"]["content"] : videoPageElements["props"]["pageProps"]["content"];

            if (content["trailer_screencap"] != null)
            {
                images.Add(new RemoteImageInfo { Url = content["trailer_screencap"].ToString() });
            }

            var fullPreviews = content.SelectToken("previews.full");
            if (fullPreviews != null && fullPreviews.Type != JTokenType.Null)
            {
                foreach (var image in fullPreviews)
                {
                    images.Add(new RemoteImageInfo { Url = image.ToString() });
                }
            }

            if (content["extra_thumbnails"] != null)
            {
                foreach (var image in content["extra_thumbnails"])
                {
                    images.Add(new RemoteImageInfo { Url = image.ToString() });
                }
            }

            if (!images.Any() && content["thumbs"] != null)
            {
                foreach (var image in content["thumbs"])
                {
                    images.Add(new RemoteImageInfo { Url = image.ToString() });
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
