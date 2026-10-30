using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Configuration;
#else
using System.Net.Http;
#endif

namespace PhoenixAdult.Providers
{
    public class ActorImageProvider : IRemoteImageProvider
    {
        private static readonly TimeSpan GFriendsCacheTtl = TimeSpan.FromHours(2);
        private static readonly SemaphoreSlim GFriendsLock = new SemaphoreSlim(1, 1);
        private static JObject gFriendsTree;
        private static DateTime gFriendsTreeTime = DateTime.MinValue;

        public string Name => Plugin.Instance.Name;

        public static async Task<List<RemoteImageInfo>> GetActorPhotos(string name, CancellationToken cancellationToken)
        {
            var tasks = new Dictionary<string, Task<string>>();
            var imageList = new List<RemoteImageInfo>();

            if (string.IsNullOrEmpty(name))
            {
                return imageList;
            }

            if (Plugin.Instance.Configuration.PreferedActorNameSource == PreferedActorNameSource.LocalDatabase)
            {
                name = Actors.ResolveWithJapaneseStyle(name);
            }

            Logger.Info($"Searching actor images for \"{name}\"");

            tasks.Add("AdultDVDEmpire", GetFromAdultDVDEmpire(name, cancellationToken));
            tasks.Add("Boobpedia", GetFromBoobpedia(name, cancellationToken));
            tasks.Add("Babepedia", GetFromBabepedia(name, cancellationToken));
            tasks.Add("IAFD", GetFromIAFD(name, cancellationToken));
            tasks.Add("Gfriends", GetFromGFriends(name, cancellationToken));

            var splitedName = name.Split();
            switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
            {
                case JAVActorNamingStyle.JapaneseStyle:
                    if (splitedName.Length > 1)
                    {
                        var nameReversed = string.Join(" ", splitedName.Reverse());

                        tasks.Add("Boobpedia 2", GetFromBoobpedia(nameReversed, cancellationToken));
                        tasks.Add("Babepedia 2", GetFromBabepedia(nameReversed, cancellationToken));
                        tasks.Add("IAFD 2", GetFromIAFD(nameReversed, cancellationToken));
                    }

                    break;
            }

            try
            {
                await Task.WhenAll(tasks.Values).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"GetActorPhotos error: \"{e}\"");

                await Analytics.Send(
                    new AnalyticsExeption
                    {
                        Request = name,
                        Exception = e,
                    }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                foreach (var image in tasks)
                {
                    var res = image.Value.Result;

                    if (!string.IsNullOrEmpty(res) && !imageList.Where(o => o.Url == res).Any())
                    {
                        imageList.Add(new RemoteImageInfo
                        {
                            ProviderName = image.Key,
                            Url = res,
                        });
                    }
                }
            }

            return imageList;
        }

        public bool Supports(BaseItem item) => item is Person;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
            => new List<ImageType>
            {
                ImageType.Primary,
            };

#if __EMBY__
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
#else
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
#endif
        {
            var images = new List<RemoteImageInfo>();

            if (item == null)
            {
                return images;
            }

            images = await GetActorPhotos(item.Name, cancellationToken).ConfigureAwait(false);

            if (item.ProviderIds.TryGetValue(this.Name, out var externalID))
            {
                var curID = externalID.Split('#');
                if (curID.Length > 2)
                {
                    var siteNum = new int[2] { int.Parse(curID[0], CultureInfo.InvariantCulture), int.Parse(curID[1], CultureInfo.InvariantCulture) };
                    var sceneID = item.ProviderIds;

                    if (sceneID.ContainsKey(this.Name))
                    {
                        var provider = Helper.GetProviderBySiteID(siteNum[0]);
                        if (provider != null)
                        {
                            IEnumerable<RemoteImageInfo> imgs = new List<RemoteImageInfo>();
                            try
                            {
                                imgs = await provider.GetImages(siteNum, curID.Skip(2).ToArray(), item, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                Logger.Error($"GetImages error: \"{e}\"");

                                await Analytics.Send(
                                    new AnalyticsExeption
                                    {
                                        Request = string.Join("#", curID.Skip(2)),
                                        SiteNum = siteNum,
                                        Exception = e,
                                    }, cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (imgs.Any())
                                {
                                    images.AddRange(imgs);
                                }
                            }
                        }
                    }
                }
            }

            images = await ImageHelper.GetImagesSizeAndValidate(images, cancellationToken).ConfigureAwait(false);

            if (images.Any())
            {
                foreach (var img in images)
                {
                    if (string.IsNullOrEmpty(img.ProviderName))
                    {
                        img.ProviderName = this.Name;
                    }
                }

                images = images.OrderByDescending(o => o.Height).ToList();
            }

            return images;
        }

#if __EMBY__
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
#else
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
#endif
        {
            return Helper.GetImageResponse(url, cancellationToken);
        }

        private static async Task<string> GetFromAdultDVDEmpire(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = HttpUtility.UrlEncode(name),
                   url = $"https://www.adultdvdempire.com/performer/search?q={encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var actorPageURL = actorData.SelectSingleText("//div[@id='performerlist']/div//a/@href");
            if (!string.IsNullOrEmpty(actorPageURL))
            {
                if (!actorPageURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    actorPageURL = "https://www.adultdvdempire.com" + actorPageURL;
                }

                var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken).ConfigureAwait(false);

                var img = actorPage.SelectSingleText("//div[contains(@class, 'performer-image-container')]/a/@href");
                if (!string.IsNullOrEmpty(img))
                {
                    image = img;
                }
            }

            return image;
        }

        private static async Task<string> GetFromBoobpedia(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = HttpUtility.UrlEncode(name),
                   url = $"http://www.boobpedia.com/wiki/index.php?search={encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var img = actorData.SelectSingleText("//table[@class='infobox']//a[@class='image']//img/@src");
            if (!string.IsNullOrEmpty(img) && !img.Contains("NoImage", StringComparison.OrdinalIgnoreCase))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = "http://www.boobpedia.com" + img;
                }

                image = img;
            }

            return image;
        }

        private static async Task<string> GetFromBabepedia(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = name.Replace(" ", "_", StringComparison.OrdinalIgnoreCase),
                   url = $"https://www.babepedia.com/babe/{encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var img = actorData.SelectSingleText("//div[@id='profimg']/a/@href");
            if (!string.IsNullOrEmpty(img) && !img.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = "https://www.babepedia.com" + img;
                }

                image = img;
            }

            return image;
        }

        /// <summary>
        /// gfriends：GitHub 上的演员头像仓库（Filetree.json 索引 + raw 图片）。
        /// 文件名即演员名，按目录顺序遍历后取最后一个命中（与上游一致）。
        /// </summary>
        private static async Task<string> GetFromGFriends(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var tree = await GetGFriendsTree(cancellationToken).ConfigureAwait(false);
            var content = tree?["Content"];
            if (content == null)
            {
                return null;
            }

            string image = null;
            foreach (var folder in content.Children<JProperty>())
            {
                if (folder.Value is not JObject files)
                {
                    continue;
                }

                foreach (var file in files.Properties())
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(file.Name), name, StringComparison.OrdinalIgnoreCase))
                    {
                        image = $"https://raw.githubusercontent.com/gfriends/gfriends/master/Content/{folder.Name}/{file.Value}";
                    }
                }
            }

            return image;
        }

        private static async Task<JObject> GetGFriendsTree(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (gFriendsTree != null && now - gFriendsTreeTime < GFriendsCacheTtl)
            {
                return gFriendsTree;
            }

            await GFriendsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                now = DateTime.UtcNow;
                if (gFriendsTree != null && now - gFriendsTreeTime < GFriendsCacheTtl)
                {
                    return gFriendsTree;
                }

                var http = await HTTP.Request("https://raw.githubusercontent.com/gfriends/gfriends/master/Filetree.json", cancellationToken).ConfigureAwait(false);
                if (http.IsOK)
                {
                    gFriendsTree = JObject.Parse(http.Content);
                    gFriendsTreeTime = DateTime.UtcNow;
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Gfriends filetree error: \"{e.Message}\"");
            }
            finally
            {
                GFriendsLock.Release();
            }

            return gFriendsTree;
        }

        private static async Task<string> GetFromIAFD(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = HttpUtility.UrlEncode(name),
                   url = $"http://www.iafd.com/results.asp?searchtype=comprehensive&searchstring={encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var actorPageURL = actorData.SelectSingleText("//table[@id='tblFem']//tbody//a/@href");
            if (!string.IsNullOrEmpty(actorPageURL))
            {
                if (!actorPageURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    actorPageURL = "http://www.iafd.com" + actorPageURL;
                }

                var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken).ConfigureAwait(false);

                var actorImage = actorPage.SelectSingleText("//div[@id='headshot']//img/@src");
                if (!actorImage.Contains("nophoto", StringComparison.OrdinalIgnoreCase))
                {
                    image = actorImage;
                }
            }

            return image;
        }
    }
}
