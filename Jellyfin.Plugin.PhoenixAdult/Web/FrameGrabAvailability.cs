#if !__EMBY__
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Configuration;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// Whether a library type enables this plugin (metadata or image provider).
    /// The item menu entry only shows on such libraries.
    /// </summary>
    internal static class FrameGrabAvailability
    {
        /// <summary>
        /// Whether the given type options enable the plugin.
        /// </summary>
        /// <param name="typeOptions">Type options, may be null.</param>
        /// <param name="pluginName">Plugin display name.</param>
        /// <returns>True when enabled.</returns>
        internal static bool IsEnabled(TypeOptions typeOptions, string pluginName)
        {
            if (typeOptions == null || string.IsNullOrEmpty(pluginName))
            {
                return false;
            }

            return Contains(typeOptions.MetadataFetchers, pluginName)
                || Contains(typeOptions.ImageFetchers, pluginName);
        }

        private static bool Contains(IEnumerable<string> values, string name)
            => values != null && values.Any(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
    }
}
#endif
