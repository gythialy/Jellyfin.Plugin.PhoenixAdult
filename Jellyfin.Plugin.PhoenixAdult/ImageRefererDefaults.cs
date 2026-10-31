using System;
using System.Collections.Generic;
using System.Linq;

namespace PhoenixAdult
{
    /// <summary>
    /// 需要 Referer 防盗链的图片主机的默认规则（格式 "host=referer"）。
    ///
    /// 这里是默认值的唯一来源：<see cref="PluginConfiguration"/> 用它做新装默认配置，
    /// <see cref="Consts.ImageReferers"/> 由它派生（运行时兜底），因此不再存在"代码里一份、设置页看不到"的硬编码。
    /// 刻意不依赖 <see cref="Consts"/> 或 <see cref="Plugin"/>：插件配置在 <see cref="Plugin"/> 构造过程中就被反序列化，
    /// 此时 <see cref="Plugin.Instance"/> 还没赋值，引用 Consts 会形成静态构造循环。
    /// </summary>
    internal static class ImageRefererDefaults
    {
        /// <summary>
        /// 默认规则行。要禁用某条规则请写成 "host="（等号后留空），而不是删掉整行。
        /// </summary>
        internal static readonly string[] Rules =
        {
            // Pornhub 图片 CDN：https://pix-cdn77.phncdn.com/... 需 pornhub 站点 Referer
            "phncdn.com=https://www.pornhub.com/",

            // JavBus 封面 /imgs/cover/*.jpg 缺站点 Referer 直接 403（实测：无 Referer 403、带 Referer 200）
            "javbus.com=https://www.javbus.com/",
        };

        /// <summary>
        /// 判断是否需要把默认规则补进配置：配置里一条有效规则都没有时返回默认规则的副本，否则返回 null。
        /// 老版本存下来的是裸的 &lt;ImageReferers /&gt;（空表），升级后靠它补齐，设置页里才能看到这些默认规则。
        /// </summary>
        /// <param name="current">配置里现有的规则行。</param>
        /// <returns>要写入的默认规则；不需要补时返回 null。</returns>
        internal static List<string> SeedIfEmpty(IEnumerable<string> current)
        {
            if (current != null && current.Any(o => !string.IsNullOrWhiteSpace(o)))
            {
                return null;
            }

            return Rules.ToList();
        }

        /// <summary>
        /// 拆一条 "host=referer" 规则；格式不对返回 false。
        /// </summary>
        /// <param name="rule">规则行。</param>
        /// <param name="host">主机片段。</param>
        /// <param name="referer">Referer 值（可为空，表示显式禁用）。</param>
        /// <returns>是否解析成功。</returns>
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
