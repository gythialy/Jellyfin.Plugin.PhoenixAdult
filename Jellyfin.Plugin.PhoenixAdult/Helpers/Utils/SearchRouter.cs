using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// Code to site routing from data/IdRoutes.json (overridable by IdRoutes.local.json);
    /// no site list in code. The first match wins, the fallback goes last.
    /// </summary>
    internal static class SearchRouter
    {
        public static bool IsFc2Id(string id)
            => id != null && id.StartsWith("FC2-PPV-", StringComparison.OrdinalIgnoreCase);

        /// <summary>Matched route name, or null.</summary>
        /// <param name="id">Normalised code.</param>
        /// <returns>Route name, or null.</returns>
        public static string GetFamily(string id) => Match(id)?.Id;

        /// <summary>Sites to query in parallel for this code, as [group, site].</summary>
        /// <param name="id">Normalised code.</param>
        /// <returns>Sites to query.</returns>
        public static IReadOnlyList<(int Group, int Site)> GetSources(string id)
        {
            var route = Match(id);
            if (route?.Sites == null)
            {
                return Array.Empty<(int Group, int Site)>();
            }

            return route.Sites
                .Where(o => o != null && o.Length == 2)
                .Select(o => (Group: o[0], Site: o[1]))
                .ToList();
        }

        /// <summary>First route whose prefix and label regex match.</summary>
        /// <param name="id">Normalised code.</param>
        /// <returns>Matched route, or null.</returns>
        public static Database.IdRouteEntry Match(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var routes = Database.IdRoutes?.Routes;
            if (routes == null || routes.Count == 0)
            {
                return null;
            }

            var label = JavId.GetLabel(id) ?? string.Empty;
            foreach (var route in routes)
            {
                if (route == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(route.IdPrefix)
                    && !id.StartsWith(route.IdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasLabelList = route.Labels != null && route.Labels.Length > 0;
                var hasLabelRegex = !string.IsNullOrEmpty(route.LabelRegex);
                if (hasLabelList || hasLabelRegex)
                {
                    var matched = hasLabelList
                        && route.Labels.Any(o => string.Equals(o, label, StringComparison.OrdinalIgnoreCase));

                    if (!matched && hasLabelRegex)
                    {
                        try
                        {
                            matched = Regex.IsMatch(label, route.LabelRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        }
                        catch (ArgumentException)
                        {
                            matched = false;
                        }
                    }

                    if (!matched)
                    {
                        continue;
                    }
                }

                return route;
            }

            return null;
        }
    }
}
