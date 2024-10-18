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
    public class SiteNewSensations : IProviderBase
    {
        private enum Site
        {
            Default,
            FamilyXXX,
            HotWifeXXX,
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var rootUrl = Helper.GetSearchSearchURL(siteNum);
            var searchResultsURLs = new List<string>();

            var searchResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken).ConfigureAwait(false);
            foreach (var searchResult in searchResults)
            {
                if (searchResult.StartsWith(rootUrl))
                {
                    searchResultsURLs.Add(searchResult);
                }
            }

            foreach (var url in searchResultsURLs)
            {
                var sceneURL = new Uri(url);
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

            if (sceneID == null || siteNum == null)
            {
                return result;
            }

            string sceneURL = Helper.Decode(sceneID[0]),
                sceneDate = string.Empty;

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var searchSite = Helper.GetSearchSiteName(siteNum);
            Logger.Info($"Search site: {searchSite}");
            var site = searchSite switch
            {
                "Family XXX" => Site.FamilyXXX,
                "HotwifeXXX" => Site.HotWifeXXX,
                _ => Site.Default
            };
            Logger.Info(site.ToString());

            if (sceneID.Length > 1)
            {
                sceneDate = sceneID[1];
            }

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("New Sensations");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            switch (site)
            {
                case Site.FamilyXXX:
                    {
                        result.Item.AddStudio("Family XXX");
                        result.Item.Name = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//h2");

                        var dateNode = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneDateP']//span").TrimEnd(',');
                        if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                        {
                            result.Item.PremiereDate = sceneDateObj;
                        }

                        var descriptionNode = sceneData.SelectNodesSafe("//div[@class='description']//p");
                        var overview = descriptionNode[0].InnerText.Trim();

                        // remove <span>Description:</span> from beginning
                        overview = overview.Substring(13);
                        result.Item.Overview = overview;

                        // performers
                        var performerNodes = sceneData.SelectNodesSafe("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneTextLink']//p//span//a");

                        foreach (var performerNode in performerNodes)
                        {
                            var performerUrl = performerNode.Attributes["href"].Value;
                            var performerPage = await HTML.ElementFromURL(performerUrl, cancellationToken).ConfigureAwait(false);
                            var performerImg = performerPage.SelectSingleNode("//div[contains(@class, 'modelBioPic')]/img");
                            result.People.Add(new PersonInfo
                            {
                                Name = performerNode.InnerText,
                                ImageUrl = performerImg.Attributes["src0_1x"].Value,
                            });
                        }

                        break;
                    }

                case Site.HotWifeXXX:
                    {
                        result.Item.AddStudio("HotwifeXXX");
                        result.Item.Name = sceneData.SelectSingleText("//div[@class='trailerInfo']/h2");

                        var dateNodeText = sceneData.SelectSingleText("//div[@class='trailerInfo']//div[contains(@class, 'released2')]");
                        var date = dateNodeText.Substring(0, dateNodeText.IndexOf(','));
                        if (DateTime.TryParseExact(date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                        {
                            result.Item.PremiereDate = sceneDateObj;
                        }

                        var descriptionNode = sceneData.SelectNodesSafe("//div[@class='dvdDescription']//p");
                        var overview = descriptionNode[0].InnerText.Trim();

                        // remove <span>Description:</span> from beginning
                        overview = overview.Substring(13);
                        result.Item.Overview = overview;

                        // performers
                        var performerNodes = sceneData.SelectNodesSafe("//div[@class='trailerInfo']//span[@class='tour_update_models']/a");

                        foreach (var performerNode in performerNodes)
                        {
                            result.People.Add(new PersonInfo
                            {
                                Name = performerNode.InnerText,
                            });
                        }

                        break;
                    }

                default:
                    {
                        result.Item.Name = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//h1");

                        var dateNode = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneDateP']//span").TrimEnd(',');
                        if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                        {
                            result.Item.PremiereDate = sceneDateObj;
                        }

                        var descriptionNodes = sceneData.SelectNodesSafe("//div[@class='description']//h2");
                        var overview = descriptionNodes[0].InnerText.Trim();
                        result.Item.Overview = overview;

                        // performers
                        var performerNodes = sceneData.SelectNodesSafe("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneTextLink']//p//span//a");

                        foreach (var performerNode in performerNodes)
                        {
                            result.People.Add(new PersonInfo
                            {
                                Name = performerNode.InnerText,
                            });
                        }

                        break;
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

            var posterNode = sceneData.SelectSingleNode("//span[@id='trailer_thumb']//span//img");
            result.Add(new RemoteImageInfo
            {
                Url = posterNode.Attributes["src"].Value,
                Type = ImageType.Primary,
            });

            return result;
        }
    }
}
