using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.ExternalId
{
    public class MovieURL : IExternalId
    {
        public string ProviderName => Plugin.Instance.Name;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        public string Key => Plugin.Instance.Name + "URL";

        public string UrlFormatString => "{0}";

        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
