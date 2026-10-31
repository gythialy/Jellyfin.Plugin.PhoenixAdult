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
  **规则不要写死在逻辑里**：默认规则放 `ImageRefererDefaults.Rules`（每行一条 `host=referer`），
  插件启动时补进 `PluginConfiguration.ImageReferers`，设置页的 Image Referer rules 里能直接看到并修改；
  `Consts.ImageReferers` 由同一份默认规则派生，只在用户规则未命中时兜底。referer 留空（`host=`）即禁用该主机。
  注意两点：① 别在 `PluginConfiguration` 构造函数里塞默认值——`XmlSerializer` 反序列化 List 是追加而非替换，
  每次读写都会多出一份重复项；② 别在配置构造期间引用 `Consts`，会触发静态构造循环（`Consts` 里有 `Plugin.Instance`）。
- **识别对话框要标来源**：多源并发检索后，同一条番号可能同时命中多个站点，用户分不清该选哪条。
  `MovieProvider.AddSourcePrefix` 按 `ProviderIds` 里的 `group#site` 用 `Helper.GetSearchSiteName` 取站名，
  在展示名前加 `[JavBus] `；只在用户发起的搜索（`fillThumbnails=true`）加，自动识别的内部搜索不加，
  避免污染 Levenshtein 匹配。展示名最终形如 `2015-08-16 · [JavBus] [Uncensored] HEYZO-0929 标题`。
- **卡片标题要剔除结构信息**：JavBus 搜索卡片的 `<span>` 里混着 `<button>`（"高清"）、`<br/>` 和两个 `<date>`
  （番号、发行日期），直接取 `InnerText` 会让番号和日期重复进标题，对话框里出现多个时间标记。
  `NetworkJavBus.ParseSearchTitle` 克隆节点后删除这些子节点再压空白；日期只通过 `PremiereDate` 走
  `AddDatePrefix` 的前缀显示一次。
- **单图站点要有横幅兜底**：JavBus 无码页只有一张封面、没有截图集，若把同一个 URL 同时当 Primary 和 Backdrop，
  Jellyfin 会按 URL 去重，横幅直接消失。`ImageHelper.GetImagesSizeAndValidate` 在"没有任何 Backdrop"时
  用海报兜底，URL 追加 `#backdrop` 片段（片段不会发到服务器，但客户端视作不同地址）；
  宽度不足 1280 时不填尺寸，避开 Jellyfin 自动下载横幅的 MinWidth 过滤。
  同一个函数里还修了 `Cleanup` 的老 bug：站点只返回横幅时 `clearImages.Where(Primary).First()` 会抛
  "Sequence contains no elements"，整个 GetImages 直接失败（新测试 `只有横幅没有海报时不做任何兜底` 覆盖）。
- **防盗链图片不能把原始地址交给 Jellyfin**：Jellyfin 下载远程图片时**不发送 Referer**（`ImageDownloader` 只带 UA），
  浏览器直连也只带本站 Referer，所以 JavBus 封面这类图直接给 `https://www.javbus.com/imgs/cover/*.jpg`
  必然 403 —— 表现是「元数据抓到了、番号也对了，但海报和背景图一格都没有」，日志里几乎没有痕迹（校验阶段是插件自己带 Referer 取的，是 200）。
  解决：插件自带图片代理 `GET /PhoenixAdult/image?u=<base64url>`（`Web/ImageProxyController`），
  只代理命中 Referer 规则的主机（其他地址直接 400），按规则补 Referer 取回字节再回传，响应带 `Cache-Control`。
  `ImageProxyUrl` 提供两种形态：`ForDownload`（服务端下载，绝对地址 `<api 根>/PhoenixAdult/image?u=…`）
  与 `ForBrowser`（识别对话框的 `<img src>`，同源相对地址）。API 根地址由 `PluginServiceRegistrator` 里的
  `applicationHost.GetLocalApiUrl("127.0.0.1", null, null)` 注入；拿不到时（如 Emby 构建）自动退回原始地址。
  接线点：`MovieImageProvider` / `ActorImageProvider` 在尺寸校验**之后**统一换地址（校验仍走原始 CDN + Referer），
  `MovieProvider` 的对话框缩略图改用 `ForBrowser`，不再"跳过需要 Referer 的图"。

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

### 2.6 识别对话框（搜索结果展示）的限制

识别对话框由客户端渲染，服务端能控制的只有 RemoteSearchResult 的几个字段：

- 只显示 **Name + SearchProviderName + ProductionYear**，**不渲染 PremiereDate**；
  SearchProviderName 还会被服务端覆盖成 provider 名（ProviderManager.GetSearchResults）。
  → 想在对话框里显示到日，只能把日期拼进 Name：MovieProvider.AddDatePrefix
  （"2026-10-20 · 标题"，排序/补图之后执行；Apply 后条目标题仍取 Update 返回的站点标题，
  因为 Jellyfin 只把 RemoteSearchResult.Name 传给 ItemLookupInfo.Name 当搜索输入）。
- 对话框里的图片是**浏览器直连** ImageUrl 加载的（不经过服务端代理、不带站点 Referer）。
  需要 Referer 的 CDN（如 phncdn.com）填进去只会显示破图，所以补图前用
  Helper.GetImageReferer(url) 过滤，命中规则就放弃补图（客户端回退到标题卡片）。
- 很多 handler 的 Search 里根本没有海报（图片只在 GetImages 里）。MovieProvider 对
  **用户发起**的搜索会给缺图结果并发取 GetImages 首图兜底（上限 10 条、单条 8s 超时、失败静默）；
  自动识别走 GetMetadata 内部调用（fillThumbnails: false），不会额外打请求。

**列表页直出海报（首选，零额外请求）**：

- 统一用 `Helper.GetImageURL(node, baseUrl)`：按 data-src / data-original / srcset / src 优先取，
  再兜底扫描 img 的自定义属性（AuntJudys/BrandNewAmateurs 用 `src0_1x`）、子元素 CSS 背景图与
  data-bg；自动补全相对地址，并过滤掉 logo / icon / sprite / placeholder / 装饰线 / `.mp4` 预览。
- handler 的 Search 里能直接拿到节点时，尽量给 `ImageUrl` 赋值（搜索结果图零成本）。
- 以下站点的列表数据本身没有图片，只能靠上面的 GetImages 兜底（不要硬造假 URL）：
  NaughtyAmerica（api.naughtyapi.com 的 JSON 只有 trailers/promo_video_data）、
  Pornhub（phncdn 的签名图必须带 Referer，对话框里是浏览器直连 → 主动跳过）、
  AnalVids / NetworkSteppedUp（Next.js JSON 无海报字段）、SitePurgatoryX（走 GetSearchResultsFromUpdate）。

**GammaEnt / AdultTime 家族（NetworkGammaEnt）的坑**：

- all_scenes 索引有 pictures，但 **all_movies 索引恒无 pictures**（只有
  movie_id / url_title / cover_path / first_clip）→ 电影类结果没有海报。
  修法：Search 结束后用一次批量 Algolia 查询（filters=movie_id=A OR movie_id=B ...）回
  all_scenes 索引，按 movie_id 取该片第一个场景的 pictures 预览图（FillMoviePreviewImages）。
- 电影封面 {movie_id}/{movie_id}_{slug}_front_400x625.jpg 只对部分影片存在
  （实测 20 部里 8 部 404），**不要**直接塞进 ImageUrl；GetImages 里仍可用它做 Primary，
  因为下载链路会校验尺寸/字节，取不到就当没有海报。
- 同一份 movies 记录里 **last_modified 恒为 JSON null**（键存在、值为 null），
  直接判断 != null 会误判成"有值"，导致电影结果没有日期；
  必须先判断 JTokenType.Null 再回退 date_created。

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

### 4.1 海报字段对照（2026-10 实测）

| 站点 | TPDB 取图方式 | 我们的做法 |
|---|---|---|
| AllAnal / NetworkSteppedUp | `networkSteppedUpJSON.py`：`scene["trailer_screencap"]`，空则 `scene["thumb"]`（同一份 `_next/data` JSON） | 已照抄（列表 JSON 直出，零额外请求） |
| AdultPrime | `networkAdultPrime.py`：`//div[contains(@class,"update-video-wrapper")]/a/div/@style` 或 `//video/@poster`，regex 取 `http…jpg` | 已照抄优先级，`<img>` 仅兜底（`img` 抓到的是 `image_midi_9.jpg`，偏小） |
| NaughtyAmerica | `networkNaughtyAmericaAPI.py`：从 `promo_video_data`/`trailers.trailer_720` 正则取 `abbrev/title`，拼 `images{1..5}.naughtycdn.com/cms/nacmscontent/v1/scenes/{abbrev}/{title}/scene/horizontal/1499x944c.{jpg,webp}` 并 HEAD 校验 | **已照抄**（2026-10-21）：原先"从 trailer 路径推 scenes/{prefix}/{name}"拼出的 URL 恒 404，场景页 `og:image` 对无 Cookie 请求返回空 202，两头都不通；现改为 TPDB 的 `cms/nacmscontent/v1` 形式 + HEAD 校验，实测 200 |
| Pornhub | `networkPornhub_*.py`：场景页 `og:image` | 相同；但 phncdn 强制 Referer，浏览器直连必 403，故对话框不填图 |
| GirlsOutWest | `siteGirlsOutWest.py`：场景页 `//div[@class="player-thumb"]//img/@src0_3x` | 列表页无图，靠补图 |
| AnalVids / MilfAF / SlutInspection | TPDB 无 scraper；实测 AnalVids 的 `/api/autocomplete/search` `terms` 只有 `type/name/url/source_id`，无图片字段 | 只能靠补图 |

## 5. 已知的"设计如此"与"站点侧问题"

| 情况 | 说明 |
|---|---|
| 文件名不含 ID 的 ID 直抓站 | ManyVids 等按 `Video/{id}` 直抓，只给演员名时天然 0 结果，属设计 |
| 子站下线 | 如 `askyourmother.com` 整站跳 `/lander`，需改走主站域名 |
| 站点改版导致搜索词无效 | 旧 slug/旧 viewkey 失效，需用新标题刷新用例 |
| 网络抖动 | 瞬时失败会伪装成"实现 bug"——**复测一次再下结论**（JacquieEtMichel 曾因此误判） |
| GetImages 本身失效（待修） | PurgatoryX（`//video[@id='main-player']` 选择器已变）、GirlsOutWest（场景页 403）、MilfAF（场景页取不到图）、SlutInspection（分页兜底分支无图）：这四个站点的**补图兜底也拿不到图**，列表页与场景页都需单独排查；回归里它们的搜索用例因此只断言"能搜到" |

## 6. 排查方法论

1. **先跑回归用例复现**，拿到确切错误（不要只看整批失败）。
2. **用 curl 复刻插件的请求**：同一 URL、同一 UA、同一 HTTP 方法、同一 Referer。
   方法差异（POST/GET）与请求头差异是最常见的隐形原因。
3. **对比 TPDB 同类站点**实现，确认是"我们过时"还是"站点变了"。
4. **修改后跑单条用例验证**，再跑全量。
5. **区分实现 bug / 站点侧问题 / 用例过时**，只有第一类才改代码。
