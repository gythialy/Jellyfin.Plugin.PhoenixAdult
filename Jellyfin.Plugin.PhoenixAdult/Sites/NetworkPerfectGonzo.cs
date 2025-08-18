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
    public class NetworkPerfectGonzo : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='itemm']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//a");
                    string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty);
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//span[@class='nm-date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    string subSite = Helper.GetSearchSiteName(siteNum);
                    var subSiteNode = node.SelectSingleNode(".//img[@class='domain-label']");
                    if (subSiteNode != null)
                    {
                        string src = subSiteNode.GetAttributeValue("src", string.Empty);
                        if (src.Contains("allinternal"))
                        {
                            subSite = "AllInternal";
                        }
                        else if (src.Contains("asstraffic"))
                        {
                            subSite = "AssTraffic";
                        }
                        else if (src.Contains("givemepink"))
                        {
                            subSite = "GiveMePink";
                        }
                        else if (src.Contains("primecups"))
                        {
                            subSite = "PrimeCups";
                        }
                        else if (src.Contains("fistflush"))
                        {
                            subSite = "FistFlush";
                        }
                        else if (src.Contains("cumforcover"))
                        {
                            subSite = "CumForCover";
                        }
                        else if (src.Contains("tamedteens"))
                        {
                            subSite = "TamedTeens";
                        }
                        else if (src.Contains("spermswap"))
                        {
                            subSite = "SpermSwap";
                        }
                        else if (src.Contains("milfthing"))
                        {
                            subSite = "MilfThing";
                        }
                        else if (src.Contains("interview"))
                        {
                            subSite = "Interview";
                        }
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [Perfect Gonzo/{subSite}] {releaseDate}",
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h2")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='col-sm-8 col-md-8 no-padding-side']/p")?.InnerText.Trim();
            movie.AddStudio("Perfect Gonzo");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='col-sm-6 col-md-6 no-padding-left no-padding-right text-right']/span");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Added", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='col-sm-8 col-md-8 no-padding-side tag-container']//a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='col-sm-3 col-md-3 col-md-offset-1 no-padding-side']/p/a");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='col-md-8 bigmodelpic']/img")?.GetAttributeValue("src", string.Empty);
                    }
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//video");
            if(posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("poster", string.Empty), Type = ImageType.Primary });
            }

            var imageNodes = detailsPageElements.SelectNodes("//ul[@class='bxslider_screenshots']//img");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("data-original", string.Empty);
                    if(!string.IsNullOrEmpty(imageUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = imageUrl });
                    }
                }
            }

            return images;
        }
    }
}
