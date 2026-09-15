#if __EMBY__
#else
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace PhoenixAdult.ExternalId
{
    public class ExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => Plugin.Instance.Name;

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item != null && item.TryGetProviderId(this.Name + "URL", out var externalUrl) && !string.IsNullOrWhiteSpace(externalUrl))
            {
                yield return externalUrl;
            }
        }
    }
}
#endif
