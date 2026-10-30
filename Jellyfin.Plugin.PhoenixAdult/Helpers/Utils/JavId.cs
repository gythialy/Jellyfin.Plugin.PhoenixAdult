using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// JAV 番号规范化：保留番号中间的 '-'（如 ABP-123、SSIS-277）。
    /// 插件上游的 Helper.GetClearTitle 会把 '-' 清洗成空格，因此所有 JAV handler
    /// 入口都必须先用本类还原成规范形态，再按站点需要转换。
    /// </summary>
    internal static class JavId
    {
        // 标准番号：厂牌（至少含一个字母）+ 可选分隔符 + 数字，如 SSIS-277 / ABP123 / 300MIUM-1234
        private static readonly Regex StandardRegex = new Regex(
            @"^(?<label>[A-Za-z0-9]*?[A-Za-z])[-_ ]?(?<number>[0-9]{2,5})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // FC2：FC2-PPV-1234567 / FC2-1234567 / FC2PPV1234567
        private static readonly Regex Fc2Regex = new Regex(
            @"^(?:FC2[-_ ]?(?:PPV)?[-_ ]?)(?<number>[0-9]{6,8})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 日期式自贩编号：012345_001 / 050422-001（分隔符原样保留）
        private static readonly Regex DateIdRegex = new Regex(
            @"^[0-9]{6}[-_][0-9]{2,3}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>尝试解析成规范番号；失败时 <paramref name="id"/> 为原输入。</summary>
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

        /// <summary>解析成规范番号；不是番号时原样返回。</summary>
        public static string Normalize(string input)
            => TryParse(input, out var id) ? id : input;

        /// <summary>拆成厂牌与数字（FC2-PPV-123 拆成 FC2 / PPV / 123 之外的段数由调用方判断）。</summary>
        public static string[] Split(string id)
            => (id ?? string.Empty).Split('-', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>取数字段（末段）。</summary>
        public static string GetNumber(string id)
        {
            var parts = Split(id);
            return parts.Length == 0 ? string.Empty : parts.Last();
        }

        /// <summary>取厂牌段（首段）。</summary>
        public static string GetLabel(string id)
        {
            var parts = Split(id);
            return parts.Length == 0 ? string.Empty : parts.First();
        }
    }
}
