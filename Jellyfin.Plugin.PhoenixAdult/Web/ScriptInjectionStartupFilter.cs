#if !__EMBY__
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PhoenixAdult.Helpers;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// 把抽帧脚本注入 Jellyfin web 客户端的 index.html：
    /// 插件无法直接改 web 目录里的静态文件（容器里通常只读），所以在管道最外层缓冲 HTML 响应后插入 script 标签。
    /// </summary>
    internal sealed class ScriptInjectionStartupFilter : IStartupFilter
    {
        /// <inheritdoc/>
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<ScriptInjectionMiddleware>();
                next(app);
            };
        }
    }

    /// <summary>
    /// 实际执行注入的中间件：只处理 index.html，失败一律回退原始响应，绝不影响页面加载。
    /// </summary>
    internal sealed class ScriptInjectionMiddleware
    {
        private const string ScriptUrl = "/PhoenixAdult/framegrab.js";

        private static readonly string[] HtmlPaths = { "/", "/web", "/web/", "/web/index.html" };

        private readonly RequestDelegate _next;

        public ScriptInjectionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!ShouldHandle(context))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // 压缩中间件在内层：去掉 Accept-Encoding，保证拿到的响应体是明文 HTML。
            var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
            context.Request.Headers.Remove("Accept-Encoding");

            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await _next(context).ConfigureAwait(false);
                buffer.Position = 0;

                if (context.Response.HasStarted
                    || context.Response.StatusCode != StatusCodes.Status200OK
                    || context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true)
                {
                    await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                    return;
                }

                string html;
                using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 4096, true))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                var injected = Inject(html);
                var bytes = Encoding.UTF8.GetBytes(injected);
                context.Response.Headers.Remove("Content-Encoding");
                context.Response.ContentLength = bytes.Length;
                await originalBody.WriteAsync(bytes).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"Script injection failed: {e.Message}");

                try
                {
                    buffer.Position = 0;
                    context.Response.ContentLength = buffer.Length;
                    await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                }
                catch (Exception inner)
                {
                    Logger.Error($"Script injection fallback failed: {inner.Message}");
                }
            }
            finally
            {
                context.Response.Body = originalBody;

                if (!string.IsNullOrEmpty(acceptEncoding))
                {
                    context.Request.Headers.AcceptEncoding = acceptEncoding;
                }
            }
        }

        private static bool ShouldHandle(HttpContext context)
        {
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                return false;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            foreach (var candidate in HtmlPaths)
            {
                if (string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Inject(string html)
        {
            if (string.IsNullOrEmpty(html) || html.Contains(ScriptUrl, StringComparison.OrdinalIgnoreCase))
            {
                return html;
            }

            var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1";
            var tag = $"<script src=\"{ScriptUrl}?v={version}\" defer></script>";

            var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return html + tag;
            }

            return html.Insert(index, tag);
        }
    }
}
#endif
