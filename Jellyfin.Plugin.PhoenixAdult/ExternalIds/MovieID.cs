using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

using MediaBrowser.Model.Providers;

namespace PhoenixAdult.ExternalId
{
    public class MovieID : IExternalId
    {
        public string ProviderName => Plugin.Instance.Name + " ID";

        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        public string Key => Plugin.Instance.Name;

        public string UrlFormatString => null;

        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
