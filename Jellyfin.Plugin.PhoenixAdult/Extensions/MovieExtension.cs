using System.Linq;
using MediaBrowser.Controller.Entities.Movies;

namespace PhoenixAdult.Extensions
{
    public static class MovieExtension
    {
        public static void AddTag(this Movie movie, string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            var tags = movie.Tags?.ToList() ?? new System.Collections.Generic.List<string>();
            if (!tags.Contains(tag, System.StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
                movie.Tags = tags.ToArray();
            }
        }

        public static void AddCollection(this Movie movie, string collection)
        {
            if (string.IsNullOrEmpty(collection))
            {
                return;
            }

            var collections = movie.Tags?.ToList() ?? new System.Collections.Generic.List<string>();
            if (!collections.Contains(collection, System.StringComparer.OrdinalIgnoreCase))
            {
                collections.Add(collection);
                movie.Tags = collections.ToArray();
            }
        }
    }
}
