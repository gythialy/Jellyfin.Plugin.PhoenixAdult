#if !__EMBY__
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Configuration;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// 判定库/条目类型是否启用了本插件：元数据下载器或图片下载器里出现 PhoenixAdult 即为启用。
    /// 条目菜单里的「抽帧」入口只在这种库上出现，避免对没在用本插件的库误操作。
    /// </summary>
    internal static class FrameGrabAvailability
    {
        /// <summary>
        /// 判断给定的类型设置是否启用了本插件。
        /// </summary>
        /// <param name="typeOptions">库设置里该条目类型的选项（可为 null）。</param>
        /// <param name="pluginName">插件显示名（<see cref="Plugin.Instance"/> 的 Name）。</param>
        /// <returns>启用返回 true。</returns>
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
