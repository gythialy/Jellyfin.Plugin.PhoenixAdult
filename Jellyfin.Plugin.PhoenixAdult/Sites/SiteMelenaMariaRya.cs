using System;
using System.Collections.Generic;
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
    public class SiteMelenaMariaRya : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var sceneID = searchTitle.Split(' ')[0];
            var sceneURL = Helper.GetSearchSearchURL(siteNum) + sceneID;
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                var titleNoFormatting = Regex.Replace(titleNode.InnerText.Split(new[] { " - Sex Movies Featuring Melena Maria Rya" }, StringSplitOptions.None)[0], @"[^A-Za-z0-9\s-]+", " ").Trim();
                var curID = Helper.Encode(sceneURL);

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = $"{titleNoFormatting} [MelenaMariaRya]",
                    SearchProviderName = Plugin.Instance.Name,
                });
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            var title = doc.DocumentNode.SelectSingleNode("//title").InnerText.Split(new[] { " - Sex Movies Featuring Melena Maria Rya" }, StringSplitOptions.None)[0];
            title = Regex.Replace(title, @"[^A-Za-z0-9\s-]", " ").Trim();
            title = Regex.Replace(title, @"\s+4\s*K(?:\s+Video)?$", string.Empty, RegexOptions.IgnoreCase);
            movie.Name = title;
            movie.Overview = doc.DocumentNode.SelectSingleNode("//meta[@name='description']").GetAttributeValue("content", string.Empty).Trim();
            movie.AddStudio("Melena Maria Rya");
            movie.AddCollection("Melena Maria Rya");
            movie.AddGenre("European");
            result.AddPerson(new PersonInfo { Name = "Melena Maria Rya", Type = PersonKind.Actor });

            var match = Regex.Match(title, @" with ([A-Z][a-z]+ [A-Z][a-z]+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                result.AddPerson(new PersonInfo { Name = match.Groups[1].Value, Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            // No images available on the site
            return await Task.FromResult(new List<RemoteImageInfo>());
        }
    }
}
