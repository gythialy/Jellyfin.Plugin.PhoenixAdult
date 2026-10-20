# 抓取实现历史经验记录

> 本文只保留**仍有参考价值的结论与教训**。已被实现取代的过程记录、重复清单、临时计划已删除
> （原 `pornrips-test-results.md`、`zero-sites-test-plan.md`、`eclass-solution.md` 三份内容高度重叠）。
>
> - 当前回归用例：`Jellyfin.Plugin.PhoenixAdult.Tests/ProviderRegression/ProviderCases.json`
> - 尚未覆盖的 handler：`docs/provider-regression-uncovered.md`
> - 用户文档（搜索语法 / 支持站点）：`docs/manualsearch.md`、`docs/sitelist.md`

## 1. 根本教训："0 结果"不等于"不可抓"（2026-08 阶段）

最初把大批站点归为 "仅 URL 直抓"，后续逐个实测证明其中相当一部分是**真实实现缺陷**：

| 站点 | 当初误判 | 实际根因 |
|---|---|---|
| SiteWatch4Beauty | 仅 URL 直抓 | search URL 写错（`/models/search?q=` 应为 `/search?q=`），且结果是内嵌 JSON 而非 HTML |
| SiteBrandNewAmateurs | 仅 URL 直抓 | 搜索入口 `/models` 应为 `/categories/movies/`，需改分页浏览 |
| NetworkPornWorld | 仅 URL 直抓 | 域名从 `ddfnetwork.com` 迁到 `pornworld.com`，search URL 与选择器同时失效 |
| NetworkSteppedUp | 无搜索接口 | `Split("and")` 区分大小写，"Skyler **And**" 没被切开 → 模型 URL 404 |
| SiteJulesJordan | 改版全失效 | 选择器全部过时，需重写 Search/Update/GetImages |
| SiteNewSensations | 仅 URL 直抓 | `descriptionNodes[0]` 越界、`posterNode` NRE 等无守卫崩溃 |

**结论：批量标注必须经真实请求验证。** 后来建立的统一回归用例正是为此：每条用例都要真实 Search 命中才算通过。

## 2. 反复出现的缺陷模式（已多次修复，写新爬虫时重点防）

### 2.1 图片 URL 拼接
- **双斜杠**：Algolia `pictures` 的值形如 `/165111/...`（自带前导斜杠），
  再用 `$".../movies/{image}"` 会拼出 `movies//165111/...` → 404。
  正确写法：`"https://images-fame.gammacdn.com/movies" + pictureUrl`（见 `NetworkGammaEnt.GetBestPictureURL`）。
- **HTML 实体未解码**：`data-poster` 等属性里的 `&` 是 `&amp;`，直接请求会被 CDN 403。
  统一用 `WebUtility.HtmlDecode`。
- **Referer 防盗链**：CDN77（`phncdn.com`）对签名 URL 校验 Referer，缺失即 403。
  `Helper.GetImageResponse` 与 `ImageHelper` 校验路径必须带同一套请求头，否则"能取到图却下载不了"。
  **规则不要写死在逻辑里**：内置规则放 `Consts.ImageReferers`（主机 → Referer 表），
  用户可在插件配置的 Image Referer rules（`PluginConfiguration.ImageReferers`，每行一条
  `host=referer`，referer 留空即禁用该主机）追加或覆盖，CDN 变更时无需改代码发版。

### 2.2 HTTP 方法 / 选择器
- **场景页只接受 GET**：Pornhub 用 POST 请求 `view_video.php` 直接返回 404 "Page Not Found"。
- **标题在子节点**：`<h1 class="title"><span class="inlineFree">…</span></h1>`，
  直接取 h1 的 `InnerText` 可能为空，需 `//h1[@class='title']/span`。
- 选择器改动优先对齐 TPDB 的 `selector_map`，比猜更快。

### 2.3 sceneID 索引
自拼 sceneID（`a|b|c`）时，**长度判断极易差一**。真实案例：`NetworkReptyle` 的 sceneID 是
`{id}|{date}|videosContent`（3 段），却用 `Length > 2` / `> 3` 取值，导致日期永远为空、类型永远回退默认。
写完务必对照 Search 里拼接处的段数。

### 2.4 空值守卫
无守卫的链式访问是最高频崩溃源：`metadata.Name.EndsWith`（`metadata` 为 null）、
`posterNode.Attributes["src"]`、`descriptionNodes[0]`、`SelectNodes(...)` 返回 null。
约定：取不到就**返回空结果**，不要把异常抛给 Jellyfin。

### 2.5 插件配置类里不能出现 IDictionary 成员
Jellyfin/Emby 用 `System.Xml.Serialization.XmlSerializer` 把插件配置持久化成 XML。
配置类里只要有一个 `Dictionary<,>` 之类实现 `IDictionary` 的成员，序列化器在反射配置类型时
就抛 `Cannot serialize member ... because it implements IDictionary`（外层是
`There was an error reflecting type ...PluginConfiguration`），于是**配置读取、配置保存、
以及所有读 `Plugin.Instance.Configuration` 的代码（含图片下载）全部失败**。
真实案例：`PluginConfiguration.ImageReferers` 曾用 `Dictionary<string, string>`，整插件配置报废。
正确做法：需要的键值配置改成 `List<string>`（每项 `key=value`）在业务代码里解析。
回归测试见 `Jellyfin.Plugin.PhoenixAdult.Tests/Configuration/PluginConfigurationSerializationTests.cs`
（构造 `new XmlSerializer(typeof(PluginConfiguration))` 即可复现该异常）。

## 3. 反爬与 FlareSolverr

站点评测：
- Cloudflare 挑战：`wowgirlsblog.com`、`kink.com`、`sexart.com`、`femjoy.com`
- 年龄验证门槛：MissaX（返回 Age Verification 页）、Kink（`?ageverified=g`）
- 限流/Turnstile：Nubiles（429）

接入范式（已在 `NetworkWowNetwork` / `SiteFemjoy` / `NetworkKink` / `NetworkMetArt` 使用）：

```csharp
string html = FlareSolverr.IsConfigured
    ? await FlareSolverr.GetHtml(url, ct)   // 浏览器上下文
    : (直连兜底);
```

- `FlareSolverr.GetJson` 用于 JSON 接口，`GetHtml` 用于分页浏览。
- FlareSolverr v2+ 的 `request.get` **忽略 headers**，不要再传。
- 测试里未配置 FlareSolverr 时，失败会被识别为 SKIP 而不是失败。

本地环境：
```bash
docker run -d --name flaresolverr -p 8191:8191 ghcr.io/gythialy/flaresolverr:latest
```

## 4. 借力 ThePornDatabase

仓库：`github.com/ThePornDatabase/scrapy`（框架）+ `scrapers` 子模块（`scenes/` 下按站点命名）。

这是排查改版问题的**第一手参考**：

- **别名/bundle 归属**：`networkTeamskeet2025.py` 的 `endpoints` 表写明每个 ES 别名对应哪个域名与哪些站点。
  漏了映射会落到默认 bundle，导致跨站乱匹配 + 拼出已下线的域名（Ask Your Mother 案例）。
- **数据源迁移**：TPDB 注释指出"较新的游览站页面已拿不到详情"，改为直接读 tours-store Elasticsearch。
  我们据此把 `NetworkReptyle` 的 Update/GetImages 改为 ES 优先、HTML 兜底。
- **选择器**：`selector_map` 里的 title/date/image/external_id 可直接对照。
- **放弃决策**：某些站点 TPDB 官方也标注不可爬（如 NetworkVNA），此时 0 结果是合理结论。

## 5. 已知的"设计如此"与"站点侧问题"

| 情况 | 说明 |
|---|---|
| 文件名不含 ID 的 ID 直抓站 | ManyVids 等按 `Video/{id}` 直抓，只给演员名时天然 0 结果，属设计 |
| 子站下线 | 如 `askyourmother.com` 整站跳 `/lander`，需改走主站域名 |
| 站点改版导致搜索词无效 | 旧 slug/旧 viewkey 失效，需用新标题刷新用例 |
| 网络抖动 | 瞬时失败会伪装成"实现 bug"——**复测一次再下结论**（JacquieEtMichel 曾因此误判） |

## 6. 排查方法论

1. **先跑回归用例复现**，拿到确切错误（不要只看整批失败）。
2. **用 curl 复刻插件的请求**：同一 URL、同一 UA、同一 HTTP 方法、同一 Referer。
   方法差异（POST/GET）与请求头差异是最常见的隐形原因。
3. **对比 TPDB 同类站点**实现，确认是"我们过时"还是"站点变了"。
4. **修改后跑单条用例验证**，再跑全量。
5. **区分实现 bug / 站点侧问题 / 用例过时**，只有第一类才改代码。
