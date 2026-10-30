#if !__EMBY__
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// 条目「抽帧」入口的后端：提供注入到 web 客户端的脚本，以及「该条目所在库是否启用了本插件」的查询。
    /// 抓帧与上传都在浏览器侧完成（canvas + Jellyfin 官方图片上传接口），这里不碰 ffmpeg。
    /// </summary>
    [ApiController]
    [Route("PhoenixAdult")]
    public class FrameGrabController : ControllerBase
    {
        private const string ScriptResourceSuffix = "Web.frameGrab.js";

        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="FrameGrabController"/> class.
        /// </summary>
        /// <param name="libraryManager">库管理器。</param>
        public FrameGrabController(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// 输出注入到 web 客户端的抽帧脚本。
        /// </summary>
        /// <returns>JavaScript 文本。</returns>
        [HttpGet("framegrab.js")]
        [AllowAnonymous]
        public ActionResult GetScript()
        {
            var assembly = typeof(FrameGrabController).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(o => o.EndsWith(ScriptResourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                return NotFound();
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null)
            {
                return NotFound();
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Content(reader.ReadToEnd(), "text/javascript; charset=utf-8");
        }

        /// <summary>
        /// 查询某条目所在库是否启用了本插件（元数据下载器或图片下载器含 PhoenixAdult）。
        /// </summary>
        /// <param name="itemId">条目 Id。</param>
        /// <returns>启用状态。</returns>
        [HttpGet("framegrab/enabled")]
        [Authorize]
        public ActionResult<FrameGrabAvailabilityResult> GetEnabled([FromQuery] Guid itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            var typeOptions = libraryOptions?.GetTypeOptions(item.GetType().Name);

            return new FrameGrabAvailabilityResult
            {
                Enabled = Plugin.Instance.Configuration.EnableFrameGrabMenu
                    && FrameGrabAvailability.IsEnabled(typeOptions, Plugin.Instance.Name),
            };
        }

        /// <summary>
        /// 「抽帧」入口的可用性。
        /// </summary>
        public class FrameGrabAvailabilityResult
        {
            /// <summary>
            /// Gets or sets a value indicating whether 该条目所在库启用了本插件且入口未被配置关闭。
            /// </summary>
            public bool Enabled { get; set; }
        }
    }
}
#endif
