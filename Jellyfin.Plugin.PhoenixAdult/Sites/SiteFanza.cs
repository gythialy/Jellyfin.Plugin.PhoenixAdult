using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// FANZA (DMM) digital content: the detail page is a Next.js SPA backed by the official
    /// GraphQL API. The scene id is the dmm content_id,
    /// derived from the code by padding to three or five digits.
    /// </summary>
    public class SiteFanza : IProviderBase
    {
        private const string GraphqlUrl = "https://api.video.dmm.co.jp/graphql";

        private const string ContentPageDataQuery = """
query ContentPageData($id: ID!, $isLoggedIn: Boolean!, $isAmateur: Boolean!, $isAnime: Boolean!, $isAv: Boolean!, $isCinema: Boolean!, $isSP: Boolean!, $shouldFetchRelatedTags: Boolean = false) {
  ppvContent(id: $id) {
    ...ContentData
    __typename
  }
  reviewSummary(contentId: $id) {
    ...ReviewSummary
    __typename
  }
  ...basketCountFragment @include(if: $isSP)
}
fragment ContentData on PPVContent {
  id
  floor
  title
  isExclusiveDelivery
  releaseStatus
  description
  notices
  isNoIndex
  isAllowForeign
  announcements {
    body
    __typename
  }
  featureArticles {
    link {
      url
      text
      __typename
    }
    __typename
  }
  packageImage {
    largeUrl
    mediumUrl
    __typename
  }
  sampleImages {
    number
    imageUrl
    largeImageUrl
    __typename
  }
  products {
    ...ProductData
    __typename
  }
  mostPopularContentImage {
    ... on ContentSampleImage {
      __typename
      largeImageUrl
      imageUrl
    }
    ... on PackageImage {
      __typename
      largeUrl
      mediumUrl
    }
    __typename
  }
  pricing {
    lowestEffectivePriceInclusiveTax
    lowestRegularPriceInclusiveTax
    sale {
      name
      id
      endAt
      __typename
    }
    pointRewardCampaign {
      name
      id
      endAt
      promotionId
      rate
      __typename
    }
    __typename
  }
  weeklyRanking: ranking(term: Weekly)
  monthlyRanking: ranking(term: Monthly)
  wishlistCount
  sample2DMovie {
    highestMovieUrl
    hlsMovieUrl
    __typename
  }
  sampleVRMovie {
    highestMovieUrl
    __typename
  }
  ...AmateurAdditionalContentData @include(if: $isAmateur)
  ...AnimeAdditionalContentData @include(if: $isAnime)
  ...AvAdditionalContentData @include(if: $isAv)
  ...CinemaAdditionalContentData @include(if: $isCinema)
  __typename
}
fragment ProductData on PPVProduct {
  id
  priority
  deliveryUnit {
    id
    priority
    streamMaxQualityGroup
    downloadMaxQualityGroup
    __typename
  }
  pricing {
    regularPriceInclusiveTax
    effectivePriceInclusiveTax
    __typename
  }
  expireDays
  utilizationStatus @include(if: $isLoggedIn)
  licenseType
  shopName
  couponDiscount {
    coupon {
      name
      expirationPolicy {
        ... on CouponExpirationAt {
          expirationAt
          __typename
        }
        ... on CouponExpirationDay {
          expirationDays
          __typename
        }
        __typename
      }
      expirationAt
      minPayment
      destinationUrl
      __typename
    }
    discountedPriceInclusiveTax
    __typename
  }
  __typename
}
fragment AmateurAdditionalContentData on PPVContent {
  deliveryStartDate
  duration
  amateurActress {
    id
    name
    imageUrl
    age
    waist
    bust
    bustCup
    height
    hip
    relatedContents {
      id
      title
      __typename
    }
    __typename
  }
  maker {
    id
    name
    __typename
  }
  label {
    id
    name
    __typename
  }
  genres {
    id
    name
    __typename
  }
  makerContentId
  playableInfo {
    ...PlayableInfo
    __typename
  }
  __typename
}
fragment PlayableInfo on PlayableInfo {
  playableDevices {
    deviceDeliveryUnits {
      id
      deviceDeliveryQualities {
        isDownloadable
        isStreamable
        __typename
      }
      __typename
    }
    device
    name
    priority
    isSupported
    __typename
  }
  deviceGroups {
    id
    devices {
      deviceDeliveryUnits {
        id
        deviceDeliveryQualities {
          isStreamable
          isDownloadable
          __typename
        }
        __typename
      }
      isSupported
      __typename
    }
    __typename
  }
  vrViewingType
  __typename
}
fragment AnimeAdditionalContentData on PPVContent {
  deliveryStartDate
  duration
  series {
    id
    name
    __typename
  }
  maker {
    id
    name
    __typename
  }
  label {
    id
    name
    __typename
  }
  genres {
    id
    name
    __typename
  }
  makerContentId
  playableInfo {
    ...PlayableInfo
    __typename
  }
  __typename
}
fragment AvAdditionalContentData on PPVContent {
  deliveryStartDate
  makerReleasedAt
  duration
  actresses {
    id
    name
    nameRuby
    imageUrl
    isBookmarked @include(if: $isLoggedIn)
    __typename
  }
  histrions {
    id
    name
    __typename
  }
  directors {
    id
    name
    __typename
  }
  series {
    id
    name
    __typename
  }
  maker {
    id
    name
    __typename
  }
  label {
    id
    name
    __typename
  }
  genres {
    id
    name
    __typename
  }
  contentType
  relatedWords @skip(if: $shouldFetchRelatedTags)
  relatedTags(limit: 16) @include(if: $shouldFetchRelatedTags) {
    ... on ContentTagGroup {
      tags {
        id
        name
        __typename
      }
      __typename
    }
    ... on ContentTag {
      id
      name
      __typename
    }
    __typename
  }
  makerContentId
  playableInfo {
    ...PlayableInfo
    __typename
  }
  __typename
}
fragment CinemaAdditionalContentData on PPVContent {
  deliveryStartDate
  duration
  actresses {
    id
    name
    nameRuby
    imageUrl
    __typename
  }
  histrions {
    id
    name
    __typename
  }
  directors {
    id
    name
    __typename
  }
  authors {
    id
    name
    __typename
  }
  series {
    id
    name
    __typename
  }
  maker {
    id
    name
    __typename
  }
  label {
    id
    name
    __typename
  }
  genres {
    id
    name
    __typename
  }
  makerContentId
  playableInfo {
    ...PlayableInfo
    __typename
  }
  __typename
}
fragment ReviewSummary on ReviewSummary {
  average
  total
  withCommentTotal
  distributions {
    total
    withCommentTotal
    rating
    __typename
  }
  __typename
}
fragment basketCountFragment on Query {
  legacyBasket @skip(if: $isLoggedIn) {
    total
    __typename
  }
  basketCount: user @include(if: $isLoggedIn) {
    ... on Member {
      ppvBasketItemCount
      __typename
    }
    __typename
  }
  __typename
}
""";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            foreach (var candidate in BuildCandidates(searchTitle))
            {
                var content = await this.GetContent(candidate, cancellationToken).ConfigureAwait(false);
                var title = (string)content?["title"];
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(candidate) } },
                    Name = title,
                    ImageUrl = (string)content?["packageImage"]?["largeUrl"],
                    PremiereDate = ParseDate((string)content?["deliveryStartDate"] ?? (string)content?["makerReleasedAt"]),
                });
                break;
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

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var contentId = Helper.Decode(sceneID[0]);
            var content = await this.GetContent(contentId, cancellationToken).ConfigureAwait(false);
            if (content == null)
            {
                return result;
            }

            result.Item.Name = (string)content["title"];
            result.Item.Overview = (string)content["description"];
            result.Item.OriginalTitle = ((string)content["makerContentId"] ?? contentId).ToUpperInvariant();
            result.Item.ExternalId = $"https://video.dmm.co.jp/av/content/?id={contentId}";

            var date = ParseDate((string)content["deliveryStartDate"] ?? (string)content["makerReleasedAt"]);
            if (date.HasValue)
            {
                result.Item.PremiereDate = date;
                result.Item.ProductionYear = date.Value.Year;
            }

            var duration = (int?)content["duration"];
            if (duration.HasValue && duration.Value > 0)
            {
                result.Item.RunTimeTicks = TimeSpan.FromSeconds(duration.Value).Ticks;
            }

            foreach (var maker in new[] { content["maker"], content["label"] })
            {
                var name = (string)maker?["name"];
                if (!string.IsNullOrEmpty(name))
                {
                    result.Item.AddStudio(name);
                }
            }

            foreach (var genre in content["genres"] as JArray ?? new JArray())
            {
                var name = (string)genre["name"];
                if (!string.IsNullOrEmpty(name))
                {
                    result.Item.AddGenre(name);
                }
            }

            foreach (var actress in content["actresses"] as JArray ?? new JArray())
            {
                var name = (string)actress["name"];
                if (!string.IsNullOrEmpty(name))
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = name, Type = PersonKind.Actor });
                }
            }

            var cover = (string)content["packageImage"]?["largeUrl"];
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var contentId = Helper.Decode(sceneID[0]);
            var content = await this.GetContent(contentId, cancellationToken).ConfigureAwait(false);
            if (content == null)
            {
                return result;
            }

            var cover = (string)content["packageImage"]?["largeUrl"];
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
            }

            foreach (var image in content["sampleImages"] as JArray ?? new JArray())
            {
                var url = (string)image["largeImageUrl"] ?? (string)image["imageUrl"];
                if (!string.IsNullOrEmpty(url))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
                }
            }

            return result;
        }

        private static IEnumerable<string> BuildCandidates(string searchTitle)
        {
            var ids = new List<string>();
            if (JavId.TryParse(searchTitle, out var javId))
            {
                var label = JavId.GetLabel(javId).ToLowerInvariant();
                var number = JavId.GetNumber(javId);
                if (int.TryParse(number, out var num))
                {
                    ids.Add($"{label}{num:000}");
                    ids.Add($"{label}{num:00000}");
                }
            }

            var compact = Regex.Match(searchTitle, @"(?<label>[A-Za-z][A-Za-z0-9]*?)[-_ ]?(?<num>[0-9]{3,5})");
            if (compact.Success)
            {
                var label = compact.Groups["label"].Value.ToLowerInvariant();
                var number = compact.Groups["num"].Value;
                ids.Add($"{label}{number}");
            }

            ids.Add(searchTitle.Trim().ToLowerInvariant());
            return ids.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct();
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }

        private async Task<JObject> GetContent(string contentId, CancellationToken cancellationToken)
        {
            var body = new JObject
            {
                ["query"] = ContentPageDataQuery,
                ["variables"] = new JObject
                {
                    ["id"] = contentId,
                    ["isLoggedIn"] = false,
                    ["isAmateur"] = false,
                    ["isAnime"] = false,
                    ["isAv"] = true,
                    ["isCinema"] = false,
                    ["isSP"] = false,
                    ["shouldFetchRelatedTags"] = true,
                },
            };

            var headers = new Dictionary<string, string>
            {
                ["Referer"] = "https://video.dmm.co.jp/",
                ["Fanza-Device"] = "BROWSER",
                ["Cache-Control"] = "no-cache",
            };

            var http = await HTTP.Request(GraphqlUrl, System.Net.Http.HttpMethod.Post, new System.Net.Http.StringContent(body.ToString(Newtonsoft.Json.Formatting.None), System.Text.Encoding.UTF8, "application/json"), cancellationToken, headers).ConfigureAwait(false);
            if (!http.IsOK)
            {
                return null;
            }

            try
            {
                return JObject.Parse(http.Content)?["data"]?["ppvContent"] as JObject;
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                return null;
            }
        }
    }
}
