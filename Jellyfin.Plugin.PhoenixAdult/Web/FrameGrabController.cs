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
    /// Backend for the frame grab entry: serves the injected script and reports whether the item's library
    /// uses this plugin. Grabbing and uploading happen in the browser.
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
        /// <param name="libraryManager">Library manager.</param>
        public FrameGrabController(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// The script injected into the web client.
        /// </summary>
        /// <returns>JavaScript text.</returns>
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
        /// Whether the item's library uses this plugin (metadata or image provider).
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <returns>Enabled state.</returns>
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

            // A duplicate install leaves the extra copy without a plugin instance.
            var plugin = Plugin.Instance;
            if (plugin?.Configuration == null)
            {
                return new FrameGrabAvailabilityResult { Enabled = false };
            }

            return new FrameGrabAvailabilityResult
            {
                Enabled = plugin.Configuration.EnableFrameGrabMenu
                    && FrameGrabAvailability.IsEnabled(typeOptions, plugin.Name),
            };
        }

        /// <summary>
        /// Availability of the frame grab entry.
        /// </summary>
        public class FrameGrabAvailabilityResult
        {
            /// <summary>
            /// Gets or sets a value indicating whether the library uses the plugin and the entry is enabled.
            /// </summary>
            public bool Enabled { get; set; }
        }
    }
}
#endif
