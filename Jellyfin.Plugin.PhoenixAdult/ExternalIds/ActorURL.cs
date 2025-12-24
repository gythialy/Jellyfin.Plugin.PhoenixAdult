using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.ExternalId
{
    public class ActorURL : IExternalId
    {
        public string ProviderName => Plugin.Instance.Name;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

        public string Key => Plugin.Instance.Name + "URL";

        public string UrlFormatString => "{0}";

        public bool Supports(IHasProviderIds item) => item is Person;
    }
}
