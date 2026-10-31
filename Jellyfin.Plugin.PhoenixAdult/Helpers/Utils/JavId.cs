using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// JAV code normalisation, keeping the hyphen inside the code.
    /// Helper.GetClearTitle strips it, so JAV handlers call this first.
    /// </summary>
    internal static class JavId
    {
        // Standard code: label with at least one letter, optional separator, digits.
        private static readonly Regex StandardRegex = new Regex(
            @"^(?<label>[A-Za-z0-9]*?[A-Za-z])[-_ ]?(?<number>[0-9]{2,5})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // FC2：FC2-PPV-1234567 / FC2-1234567 / FC2PPV1234567
        private static readonly Regex Fc2Regex = new Regex(
            @"^(?:FC2[-_ ]?(?:PPV)?[-_ ]?)(?<number>[0-9]{6,8})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Date style self-published codes: 012345_001 / 050422-001.
        private static readonly Regex DateIdRegex = new Regex(
            @"^[0-9]{6}[-_][0-9]{2,3}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Try to parse a canonical code; on failure <paramref name="id"/> keeps the input.</summary>
        public static bool TryParse(string input, out string id)
        {
            id = input;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var value = input.Trim();

            var fc2 = Fc2Regex.Match(value);
            if (fc2.Success)
            {
                id = $"FC2-PPV-{fc2.Groups["number"].Value}";
                return true;
            }

            if (DateIdRegex.IsMatch(value))
            {
                id = value;
                return true;
            }

            var standard = StandardRegex.Match(value);
            if (standard.Success)
            {
                id = $"{standard.Groups["label"].Value.ToUpperInvariant()}-{standard.Groups["number"].Value}";
                return true;
            }

            return false;
        }

        /// <summary>Canonical code, or the input when it is not a code.</summary>
        public static string Normalize(string input)
            => TryParse(input, out var id) ? id : input;

        /// <summary>Split into label and digits.</summary>
        public static string[] Split(string id)
            => (id ?? string.Empty).Split('-', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Last segment (digits).</summary>
        public static string GetNumber(string id)
        {
            var parts = Split(id);
            return parts.Length == 0 ? string.Empty : parts.Last();
        }

        /// <summary>First segment (label).</summary>
        public static string GetLabel(string id)
        {
            var parts = Split(id);
            return parts.Length == 0 ? string.Empty : parts.First();
        }
    }
}
