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
    public class SiteFamilyTherapy : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            // This provider in the python version has a fallback to Clips4Sale which is not implemented here due to its complexity.
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace("and", "&");
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var searchNodes = searchPageElements.SelectNodes("//article");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode("./h2/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", ""));
                    var dateNode = node.SelectSingleNode("./p/span[1]");
                    string releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        releaseDate = parsedDate.ToString("MMM dd, yyyy");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [FamilyTherapy] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
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
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();

            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='entry-content']/p[1]") ?? detailsPageElements.SelectSingleNode("//div[@class='entry-content']");
            if(summaryNode != null)
                movie.Overview = summaryNode.InnerText.Trim();

            movie.AddStudio("Family Therapy");
            movie.AddTag("Family Therapy");
            movie.AddCollection(new[] { "Family Therapy" });

            var genreNodes = detailsPageElements.SelectNodes("//a[@rel='category tag']");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var dateNode = detailsPageElements.SelectSingleNode("//p[@class='post-meta']/span");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='entry-content']/p[contains(text(),'starring') or contains(text(), 'Starring')]");
            if(actorNodes != null)
            {
                foreach(var actorNode in actorNodes)
                {
                    var match = Regex.Match(actorNode.InnerText, @"(?<=[Ss]tarring\s)\w*\s\w*(\s&\s\w*\s\w*)*");
                    if (match.Success)
                    {
                        var actors = match.Value.Split('&');
                        foreach(var actor in actors)
                            result.People.Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(new List<RemoteImageInfo>());
        }
    }
}
