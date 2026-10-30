using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// 番号 → 站点 的路由表解析：规则全部来自 data/IdRoutes.json（可被 IdRoutes.local.json 覆盖），
    /// 代码里不再写死站点清单。第一个命中的路由生效，兜底路由放最后。
    /// </summary>
    internal static class SearchRouter
    {
        public static bool IsFc2Id(string id)
            => id != null && id.StartsWith("FC2-PPV-", StringComparison.OrdinalIgnoreCase);

        /// <summary>命中路由的名字（madou / jav / fc2 / 自定义），未命中返回 null。</summary>
        /// <param name="id">规范化后的番号。</param>
        /// <returns>路由名或 null。</returns>
        public static string GetFamily(string id) => Match(id)?.Id;

        /// <summary>按路由表返回该番号需要并发检索的站点（[组号, 站点号]）。</summary>
        /// <param name="id">规范化后的番号。</param>
        /// <returns>需要并发检索的站点集合。</returns>
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

        /// <summary>按路由表顺序匹配 prefix 与 label 正则，返回第一条命中的路由。</summary>
        /// <param name="id">规范化后的番号。</param>
        /// <returns>命中的路由，未命中返回 null。</returns>
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

                if (!string.IsNullOrEmpty(route.LabelRegex))
                {
                    try
                    {
                        if (!Regex.IsMatch(label, route.LabelRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        {
                            continue;
                        }
                    }
                    catch (ArgumentException)
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
