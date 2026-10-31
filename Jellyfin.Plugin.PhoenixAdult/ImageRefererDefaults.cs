using System;
using System.Collections.Generic;
using System.Linq;

namespace PhoenixAdult
{
    /// <summary>
    /// Default referer rules (host=referer) for image hosts that enforce a Referer check.
    ///
    /// Single source of truth: the plugin configuration seeds from it and
    /// <see cref="Consts.ImageReferers"/> derives from it.
    /// It avoids Consts and Plugin on purpose: configuration is deserialized inside the
    /// Plugin constructor, before Plugin.Instance exists.
    /// </summary>
    internal static class ImageRefererDefaults
    {
        /// <summary>
        /// Disable one rule with "host=" (empty value) instead of deleting the line.
        /// </summary>
        internal static readonly string[] Rules =
        {
            // Pornhub image CDN.
            "phncdn.com=https://www.pornhub.com/",

            // JavBus covers answer 403 without the site Referer.
            "javbus.com=https://www.javbus.com/",
        };

        /// <summary>
        /// Defaults to write when the configuration has no rule at all; null otherwise.
        /// </summary>
        internal static List<string> SeedIfEmpty(IEnumerable<string> current)
        {
            if (current != null && current.Any(o => !string.IsNullOrWhiteSpace(o)))
            {
                return null;
            }

            return Rules.ToList();
        }

        /// <summary>
        /// Split a host=referer rule; false when malformed.
        /// </summary>
        internal static bool TryParse(string rule, out string host, out string referer)
        {
            host = null;
            referer = null;
            if (string.IsNullOrWhiteSpace(rule))
            {
                return false;
            }

            var separator = rule.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return false;
            }

            host = rule.Substring(0, separator).Trim();
            referer = rule.Substring(separator + 1).Trim();
            return host.Length > 0;
        }
    }
}
