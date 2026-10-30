using System;
using System.Collections.Generic;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// 多源搜索路由：识别番号类型，返回应该并发检索的站点列表。
    /// 解决"一个番号只能绑一个站点"的问题（如 MDSR-0005 可能 MadouQu / ModelMediaAsia / AVBase 都有）。
    /// 站点编号与 data/SiteList.json 保持一致。
    /// </summary>
    internal static class SearchRouter
    {
        // 麻豆系：MadouQu、ModelMediaAsia、AVBase（兜底 JAV 误判）、JavBus（兜底）
        private static readonly (int Group, int Site)[] MadouSources =
        {
            (234, 0), // MadouQu
            (235, 0), // ModelMediaAsia
            (239, 0), // AVBase
            (96, 0),  // JavBus
        };

        // 标准 JAV 番号：聚合站/厂牌直取，按命中率排序
        private static readonly (int Group, int Site)[] JavSources =
        {
            (239, 0), // AVBase（番号→多站，首选）
            (96, 0),  // JavBus
            (38, 0),  // JAVLibrary
            (17, 0),  // R18
            (163, 0), // JAVDatabase
            (240, 0), // DUGA
            (251, 0), // FANZA
        };

        // FC2：官方 + 镜像/数据库
        private static readonly (int Group, int Site)[] Fc2Sources =
        {
            (238, 0), // FC2 官方
            (242, 0), // JavFree
            (249, 0), // FC2PPVDB
            (248, 0), // FC2Hub
        };

        public static bool IsFc2Id(string id)
            => id != null && id.StartsWith("FC2-PPV-", StringComparison.OrdinalIgnoreCase);

        public static bool IsMadouId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            var label = JavId.GetLabel(id);
            return label.StartsWith("MD", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("MTVQ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>番号所属 family（madou / jav / fc2）。</summary>
        /// <param name="id">规范化后的番号。</param>
        /// <returns>family 名称。</returns>
        public static string GetFamily(string id)
        {
            if (IsFc2Id(id))
            {
                return "fc2";
            }

            return IsMadouId(id) ? "madou" : "jav";
        }

        /// <summary>按番号类型返回并发检索的站点；overrides 可覆盖某个 family 的默认清单。</summary>
        /// <param name="id">规范化后的番号。</param>
        /// <param name="overrides">覆盖清单（family=group:site|group:site;...），可为空。</param>
        /// <returns>需要并发检索的站点集合。</returns>
        public static IReadOnlyList<(int Group, int Site)> GetSources(string id, string overrides = null)
        {
            var family = GetFamily(id);
            var custom = ParseOverrides(overrides);
            if (custom.TryGetValue(family, out var list) && list.Count > 0)
            {
                return list;
            }

            switch (family)
            {
                case "fc2":
                    return Fc2Sources;
                case "madou":
                    return MadouSources;
                default:
                    return JavSources;
            }
        }

        /// <summary>
        /// 解析 "family=group:site|group:site;family=..." 形式的覆盖清单；非法片段忽略。
        /// </summary>
        /// <param name="text">配置中的覆盖串。</param>
        /// <returns>family 到站点集合的映射。</returns>
        public static Dictionary<string, List<(int Group, int Site)>> ParseOverrides(string text)
        {
            var result = new Dictionary<string, List<(int Group, int Site)>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2)
                {
                    continue;
                }

                var sites = new List<(int Group, int Site)>();
                foreach (var item in pair[1].Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var gs = item.Split(':');
                    if (gs.Length == 2
                        && int.TryParse(gs[0].Trim(), out var group)
                        && int.TryParse(gs[1].Trim(), out var site))
                    {
                        sites.Add((group, site));
                    }
                }

                if (sites.Count > 0)
                {
                    result[pair[0].Trim()] = sites;
                }
            }

            return result;
        }
    }
}
