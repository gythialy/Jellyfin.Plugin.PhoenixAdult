using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;

namespace PhoenixAdult.ExternalId
{
    public class ExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "PhoenixAdult";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.ProviderIds.TryGetValue("PhoenixAdultURL", out var url) && !string.IsNullOrEmpty(url))
            {
                yield return url;
            }
        }
    }
}
