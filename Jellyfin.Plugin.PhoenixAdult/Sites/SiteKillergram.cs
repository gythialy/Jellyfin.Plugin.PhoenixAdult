using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Plugin.PhoenixAdult.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.Sites
{
    public class SiteKillergram : IProviderBase
    {
        private const string SiteName = "Killergram";
        private const string BaseUrl = "https://www.killergram.com";

        public Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var sceneId = searchTitle.Split(' ')[0];
            var doc = FetchPageContent(sceneId);
            var titleNoFormatting = ExtractTitle(doc);
            var releaseDate = DateTime.Parse(ExtractDate(doc)).ToString("dd MMMM yyyy");

            var searchResults = new List<RemoteSearchResult>
            {
                new RemoteSearchResult
                {
                    Id = $"{sceneId}|{siteNum[0]}",
                    Name = $"[{SiteName}] {titleNoFormatting} - {releaseDate}",
                    Score = 100
                }
            };

            return Task.FromResult(searchResults);
        }

        public Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var sceneId = sceneID[0].Split('|')[0];
            var doc = FetchPageContent(sceneId);

            var metadataResult = new MetadataResult<BaseItem>
            {
                Item = new BaseItem(),
                HasMetadata = true
            };

            metadataResult.Item.Name = ExtractTitle(doc);
            metadataResult.Item.Overview = ExtractSummary(doc);
            metadataResult.Item.AddStudio(SiteName);
            metadataResult.Item.Tagline = SiteName;
            metadataResult.Item.AddCollection(SiteName);
            metadataResult.Item.PremiereDate = DateTime.Parse(ExtractDate(doc));
            metadataResult.Item.AddGenre("British");

            foreach (var actor in ExtractActors(doc))
            {
                metadataResult.AddPerson(new PersonInfo { Name = actor, Type = PersonType.Actor });
            }

            return Task.FromResult(metadataResult);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var sceneId = sceneID[0].Split('|')[0];
            var doc = FetchPageContent(sceneId);
            var art = new List<string>();
            ExtractImages(doc, art);

            var list = new List<RemoteImageInfo>();
            foreach (var imageUrl in art)
            {
                list.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(list);
        }

        private static HtmlDocument FetchPageContent(string sceneId)
        {
            var sceneUrl = $"{BaseUrl}/gals/movie.php?s={sceneId}";
            var doc = new HtmlDocument();
            doc.LoadHtml(new PhoenixAdultHttpClient().Get(sceneUrl));
            return doc;
        }

        private static string ExtractTitle(HtmlDocument doc)
        {
            var searchResult = doc.DocumentNode.SelectSingleNode("//img[@id='episode_001']").GetAttributeValue("src", string.Empty);
            var titleMatches = Regex.Match(searchResult.Trim(), @"https://media.killergram.com/models/(?<actress>[\w ]+)/(?P=actress)_(?<title>[\w ]+)/.*");
            return titleMatches.Groups["title"].Value;
        }

        private static string ExtractDate(HtmlDocument doc)
        {
            var searchResult = doc.DocumentNode.SelectSingleNode("//span[@class='episodeheader' and text()[contains(., 'published')]]/parent::node()/text()");
            return searchResult.InnerText.Trim();
        }

        private static string ExtractSummary(HtmlDocument doc)
        {
            var searchResult = doc.DocumentNode.SelectSingleNode("//table[@class='episodetext']//tr[5]/td[2]/text()");
            return searchResult.InnerText.Trim();
        }

        private static IEnumerable<string> ExtractActors(HtmlDocument doc)
        {
            var actors = new List<string>();
            var actorResults = doc.DocumentNode.SelectNodes("//span[@class='episodeheader' and text()[contains(., 'starring')]]/parent::node()/span[@class='modelstarring']/a/text()");
            if (actorResults != null)
            {
                foreach (var actor in actorResults)
                {
                    actors.Add(actor.InnerText.Trim());
                }
            }
            return actors;
        }

        private static void ExtractImages(HtmlDocument doc, ICollection<string> art)
        {
            var prevImage = 0;
            var isImage = true;
            while (isImage)
            {
                var currImage = prevImage + 1;
                var imageResult = doc.DocumentNode.SelectSingleNode($"//img[@id='episode_{currImage:D3}']");
                if (imageResult != null && !string.IsNullOrEmpty(imageResult.GetAttributeValue("src", string.Empty)))
                {
                    art.Add(imageResult.GetAttributeValue("src", string.Empty).Trim());
                    prevImage = currImage;
                }
                else
                {
                    isImage = false;
                }
            }
        }
    }
}
