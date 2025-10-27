using System;
using System.Collections.Generic;
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
    public class SiteWicked : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(' ', '-');
            if (!encodedTitle.Contains("/"))
            {
                encodedTitle = encodedTitle.Replace("-", "/");
            }

            if (!encodedTitle.ToLower().Contains("scene"))
            {
                var searchHttp = await HTTP.Request($"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}", HttpMethod.Get, cancellationToken);
                if (searchHttp.IsOK)
                {
                    var searchDoc = new HtmlDocument();
                    searchDoc.LoadHtml(searchHttp.Content);
                    var searchResults = searchDoc.DocumentNode.SelectNodes("//div[@class='sceneContainer']");
                    if (searchResults != null)
                    {
                        foreach (var searchResult in searchResults)
                        {
                            var titleNoFormatting = searchResult.SelectSingleNode(".//h3").InnerText.Trim().TitleCase().Replace("Xxx", "XXX");
                            var curId = Helper.Encode(searchResult.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty));
                            var releaseDate = DateTime.Parse(searchResult.SelectSingleNode(".//p[@class='sceneDate']").InnerText.Trim()).ToString("yyyy-MM-dd");
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                                Name = $"{titleNoFormatting} [Wicked/Scene] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }

                    var dvdTitle = searchDoc.DocumentNode.SelectSingleNode("//h3[@class='dvdTitle']")?.InnerText.Trim().TitleCase().Replace("Xxx", "XXX");
                    if (!string.IsNullOrEmpty(dvdTitle))
                    {
                        var curId = Helper.Encode(searchDoc.DocumentNode.SelectSingleNode("//link[@rel='canonical']").GetAttributeValue("href", string.Empty));
                        var releaseDate = DateTime.Parse(searchDoc.DocumentNode.SelectSingleNode("//li[@class='updatedOn']").InnerText.Replace("Updated", "").Trim()).ToString("yyyy-MM-dd");
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{dvdTitle} [Wicked/Full Movie] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }
            else
            {
                var searchHttp = await HTTP.Request($"{Helper.GetSearchBaseURL(siteNum)}/en/video/{encodedTitle}", HttpMethod.Get, cancellationToken);
                if (searchHttp.IsOK)
                {
                    var searchDoc = new HtmlDocument();
                    searchDoc.LoadHtml(searchHttp.Content);
                    var titleNoFormatting = searchDoc.DocumentNode.SelectSingleNode("//h1//span").InnerText.Trim().TitleCase().Replace("Xxx", "XXX");
                    var curId = Helper.Encode(searchDoc.DocumentNode.SelectSingleNode("//link[@rel='canonical']").GetAttributeValue("href", string.Empty));
                    var releaseDate = DateTime.Parse(searchDoc.DocumentNode.SelectSingleNode("//li[@class='updatedDate']").InnerText.Replace("Updated", "").Replace("|", "").Trim()).ToString("yyyy-MM-dd");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [Wicked/Scene] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            var movie = (Movie)result.Item;
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1//span").InnerText.Trim().TitleCase().Replace("Xxx", "XXX");
            movie.AddStudio("Wicked Pictures");
            var dateNode = doc.DocumentNode.SelectSingleNode("//li[@class='updatedOn'] | //li[@class='updatedDate']");
            var date = dateNode?.InnerText.Replace("Updated", "").Replace("|", "").Trim();
            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            if (sceneURL.Contains("/video/"))
            {
                var genreNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'sceneColCategories')]/a");
                if (genreNodes != null)
                {
                    foreach (var genre in genreNodes)
                    {
                        movie.AddGenre(genre.InnerText.Trim());
                    }
                }

                var actorNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'sceneColActors')]//a");
                if (actorNodes != null)
                {
                    foreach (var actor in actorNodes)
                    {
                        var actorName = actor.InnerText.Trim();
                        var actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", string.Empty);
                        var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var actorPhotoUrl = actorDoc.DocumentNode.SelectSingleNode("//img[@class='actorPicture']")?.GetAttributeValue("src", string.Empty);
                            result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
                        }
                    }
                }

                var dvdPageUrl = Helper.GetSearchBaseURL(siteNum) + doc.DocumentNode.SelectSingleNode("//div[@class='content']//a[contains(@class, 'dvdLink')]").GetAttributeValue("href", string.Empty);
                var dvdHttp = await HTTP.Request(dvdPageUrl, HttpMethod.Get, cancellationToken);
                if (dvdHttp.IsOK)
                {
                    var dvdDoc = new HtmlDocument();
                    dvdDoc.LoadHtml(dvdHttp.Content);
                    var tagline = dvdDoc.DocumentNode.SelectSingleNode("//h3[@class='dvdTitle']").InnerText.Trim().TitleCase().Replace("Xxx", "XXX");
                    movie.AddTag(tagline);
                    movie.AddCollection(tagline);
                    movie.Overview = dvdDoc.DocumentNode.SelectSingleNode("//p[@class='descriptionText']")?.InnerText.Trim();
                    var directorNodes = dvdDoc.DocumentNode.SelectNodes("//ul[@class='directedBy']");
                    if (directorNodes != null)
                    {
                        foreach (var director in directorNodes)
                        {
                            result.People.Add(new PersonInfo { Name = director.InnerText.Trim(), Type = PersonKind.Director });
                        }
                    }
                }
            }
            else
            {
                var genreNodes = doc.DocumentNode.SelectNodes("//p[@class='dvdCol']/a");
                if (genreNodes != null)
                {
                    foreach (var genre in genreNodes)
                    {
                        movie.AddGenre(genre.InnerText.Trim());
                    }
                }

                var actorNodes = doc.DocumentNode.SelectNodes("//div[@class='actorCarousel']//a");
                if (actorNodes != null)
                {
                    foreach (var actor in actorNodes)
                    {
                        var actorName = actor.SelectSingleNode(".//span").InnerText.Trim();
                        var actorPhotoUrl = actor.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);
                        result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
                    }
                }

                movie.AddTag("Wicked Pictures");
                movie.AddCollection("Wicked Pictures");
                movie.Overview = doc.DocumentNode.SelectSingleNode("//p[@class='descriptionText']")?.InnerText.Trim();
                var directorNodes = doc.DocumentNode.SelectNodes("//ul[@class='directedBy']");
                if (directorNodes != null)
                {
                    foreach (var director in directorNodes)
                    {
                        result.People.Add(new PersonInfo { Name = director.InnerText.Trim(), Type = PersonKind.Director });
                    }
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

            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken);
            if (!http.IsOK)
            {
                return images;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            if (sceneURL.Contains("/video/"))
            {
                var scriptText = doc.DocumentNode.SelectSingleNode("//script[contains(., 'picPreview')]").InnerText;
                var alpha = scriptText.IndexOf("picPreview\":\"") + 13;
                var omega = scriptText.IndexOf("\"", alpha);
                var previewBG = scriptText.Substring(alpha, omega - alpha).Replace("\\/", "/");
                images.Add(new RemoteImageInfo { Url = previewBG, Type = ImageType.Backdrop });
                var dvdPageUrl = Helper.GetSearchBaseURL(siteNum) + doc.DocumentNode.SelectSingleNode("//div[@class='content']//a[contains(@class, 'dvdLink')]").GetAttributeValue("href", string.Empty);
                var dvdHttp = await HTTP.Request(dvdPageUrl, HttpMethod.Get, cancellationToken);
                if (dvdHttp.IsOK)
                {
                    var dvdDoc = new HtmlDocument();
                    dvdDoc.LoadHtml(dvdHttp.Content);
                    var dvdCover = dvdDoc.DocumentNode.SelectSingleNode("//img[@class='dvdCover']").GetAttributeValue("src", string.Empty);
                    images.Add(new RemoteImageInfo { Url = dvdCover, Type = ImageType.Primary });
                }

                var photoPageUrl = Helper.GetSearchBaseURL(siteNum) + doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'picturesItem')]//a").GetAttributeValue("href", string.Empty).Split('?')[0];
                var photoHttp = await HTTP.Request(photoPageUrl, HttpMethod.Get, cancellationToken);
                if (photoHttp.IsOK)
                {
                    var photoDoc = new HtmlDocument();
                    photoDoc.LoadHtml(photoHttp.Content);
                    var poster = photoDoc.DocumentNode.SelectSingleNode("//div[@class='previewImage']//img").GetAttributeValue("src", string.Empty);
                    images.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Primary });
                    var extraPix = photoDoc.DocumentNode.SelectNodes("//li[@class='preview']//a[@class='imgLink pgUnlocked']");
                    if (extraPix != null)
                    {
                        foreach (var pic in extraPix)
                        {
                            images.Add(new RemoteImageInfo { Url = pic.GetAttributeValue("href", string.Empty), Type = ImageType.Backdrop });
                        }
                    }
                }
            }
            else
            {
                var scenePreviews = doc.DocumentNode.SelectNodes("//div[@class='sceneContainer']//img[contains(@id, 'clip')]");
                if (scenePreviews != null)
                {
                    foreach (var scenePreview in scenePreviews)
                    {
                        var previewIMG = scenePreview.GetAttributeValue("data-original", string.Empty).Split('?')[0];
                        images.Add(new RemoteImageInfo { Url = previewIMG, Type = ImageType.Backdrop });
                    }
                }

                var dvdCover = doc.DocumentNode.SelectSingleNode("//img[@class='dvdCover']").GetAttributeValue("src", string.Empty);
                images.Add(new RemoteImageInfo { Url = dvdCover, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
