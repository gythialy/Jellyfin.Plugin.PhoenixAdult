#if !__EMBY__
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// Decides which loaded copy of this assembly owns the frame grab endpoints.
    /// A manual build left next to the auto-updated one makes Jellyfin load the assembly
    /// twice, and both copies would register the same MVC route: every request would then
    /// fail with AmbiguousMatchException.
    /// </summary>
    internal static class FrameGrabRegistration
    {
        /// <summary>
        /// Whether this copy is the newest loaded copy of its own assembly.
        /// </summary>
        /// <param name="self">This copy.</param>
        /// <param name="loaded">Every loaded copy.</param>
        /// <returns>True when no newer copy is loaded.</returns>
        internal static bool ShouldOwnEndpoints(
            (string Name, Version Version, string Location) self,
            IEnumerable<(string Name, Version Version, string Location)> loaded)
        {
            if (loaded == null)
            {
                return true;
            }

            foreach (var other in loaded)
            {
                if (string.Equals(other.Name, self.Name, StringComparison.OrdinalIgnoreCase)
                    && Compare(other, self) > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether this assembly is the newest loaded copy of itself.
        /// </summary>
        /// <param name="self">This assembly.</param>
        /// <param name="duplicates">Paths of the other loaded copies, empty when unique.</param>
        /// <returns>True when this is the copy that should register the endpoints.</returns>
        internal static bool ShouldOwnEndpoints(Assembly self, out string[] duplicates)
        {
            var mine = Describe(self);
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(Describe).ToArray();

            duplicates = loaded
                .Where(o => string.Equals(o.Name, mine.Name, StringComparison.OrdinalIgnoreCase))
                .Where(o => o.Version != mine.Version || !string.Equals(o.Location, mine.Location, StringComparison.Ordinal))
                .Select(o => string.IsNullOrEmpty(o.Location) ? o.Version?.ToString() : o.Location)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return ShouldOwnEndpoints(mine, loaded);
        }

        /// <summary>
        /// Total order between copies: newer version wins, then the smaller path keeps the
        /// result stable across restarts so exactly one copy registers.
        /// </summary>
        /// <param name="left">Left copy.</param>
        /// <param name="right">Right copy.</param>
        /// <returns>Positive when left outranks right.</returns>
        private static int Compare(
            (string Name, Version Version, string Location) left,
            (string Name, Version Version, string Location) right)
        {
            var byVersion = Comparer<Version>.Default.Compare(left.Version, right.Version);
            if (byVersion != 0)
            {
                return byVersion;
            }

            return string.CompareOrdinal(right.Location ?? string.Empty, left.Location ?? string.Empty);
        }

        private static (string Name, Version Version, string Location) Describe(Assembly assembly)
        {
            var name = assembly.GetName();

            string location;
            try
            {
                location = assembly.IsDynamic ? string.Empty : assembly.Location;
            }
            catch (NotSupportedException)
            {
                location = string.Empty;
            }

            return (name.Name ?? string.Empty, name.Version, location ?? string.Empty);
        }
    }
}
#endif
