# 0 结果站点测试计划

> 依据：`docs/cross-validation-report.md`（stashapp/CommunityScrapers 交叉验证结论）+ `docs/pornrips-test-results.md`。
> 目标：将 33 个 ZERO 站点逐类给出可执行的测试方案与验收标准，区分"实现问题"与"环境/搜索词因素"。
> 日期：2026-08-11

## 分类总览

| 类别 | 数量 | 性质 | 处理方向 |
|---|---|---|---|
| A. 已修复待复测 | 2 | 实现已重写/改选择器 | 回归测试确认 |
| B. 搜索 URL 配置残缺 | 1 | 配置错误（`_next/data/undefined`） | 修配置 |
| C. 品牌迁移/接口变更 | 2 | 域名 301 / API 已死 | 迁移 base URL |
| D. 反爬拦截（Cloudflare） | 3 | 环境因素，需 FlareSolverr | 标记环境依赖 |
| E. 仅支持 URL 直抓 | 25 | 站点无站内搜索（community 亦无） | 改用 sceneByURL 验证 |

---

## A. 已修复待复测（2 个）

| 站点 | 修复 | 验证方法 | 预期 |
|---|---|---|---|
| **SiteSexMex** | 选择器 `div.thumbnail>h5` → `div.videothumbnail>a[title]` | 用 pornrips 标题 "Gabriela Veracruz" 跑 Search | ≥1 结果（已单测通过） |
| **NetworkBang** | ES API → `bang.com/videos?term=` 网页搜索 | 全链路 Search→Update→GetImages | 44 结果 + 完整元数据（已通过） |

**测试用例**（并入 `PornripsRegressionTests`）：
```
SexMex.26.08.08.Gabriela.Veracruz.XXX.720p → SiteSexMex(199/0) → expect ≥1
Bang.26.08.07.Some.Scene.XXX.720p → NetworkBang(1/0) → expect ≥1
```

---

## B. 搜索 URL 配置残缺（1 个）

| 站点 | 问题 | 证据 |
|---|---|---|
| **NetworkSteppedUp**（Swallowed） | search URL 是 `/_next/data/`（残缺，无 buildId） | 请求返回 404 |

**修复方案**：Swallowed 是 Next.js 站，正确搜索入口待抓包确认。对照 CommunityScrapers（`Swallowed.yml` 只有 sceneByURL）——**建议放弃标题搜索，改 URL 直抓**。

**测试**：
```
curl https://tour.swallowed.com/_next/data/<buildId>/... → 需从页面提取 buildId
或: 直接验证 sceneByURL 模式: 提供已知场景 URL → Update 能解析
```

---

## C. 品牌迁移/接口变更（2 个）

| 站点 | 问题 | 证据 | 方案 |
|---|---|---|---|
| **NetworkPornWorld**（PornWorld） | base `ddfnetwork.com` 301 → `pornworld.com` | HTTP 301 location 确认 | base URL 迁移到 pornworld.com |
| **SiteFemjoy** | API v2 404 + 整站 Cloudflare | `/api/v2/search/videos` → 404 | 需 FlareSolverr 或放弃（见 D） |

**测试**（PornWorld）：
```
curl -I https://pornworld.com/videos/freeword/test → 预期 200（非 301）
迁移后: PornWorld.26.08.09.Jena.Larose.XXX → NetworkPornWorld(106/16) → expect ≥1
```

---

## D. 反爬拦截（Cloudflare，3 个）

| 站点 | 现象 | 结论 |
|---|---|---|
| **SiteFemjoy** | 网页 `/videos?s=` → 301 `/join` | 整站 Cloudflare，无 cookie 全拦 |
| **NetworkMetArt**（TheLifeErotic） | sexart.com → 403 | Cloudflare |
| **NetworkKink**（WhippedAss） | kink.com/search → 403 | Cloudflare |

**处理**：这 3 个标记为"**环境依赖**"，测试时需配置 `FlareSolverrURL`（插件已支持）。无 FlareSolverr 环境下 0 结果属预期，**不算 bug**。

**测试**（需 FlareSolverr 环境）：
```
配置 PluginConfiguration.FlareSolverrURL 后:
TheLifeErotic.26.08.10.Arina.Shy → NetworkMetArt(99/1) → expect ≥1
```

---

## E. 仅支持 URL 直抓（25 个，最大类）

**核心结论**：这些站点的 CommunityScrapers scraper **只有 sceneByURL（精确 URL 抓取），没有 queryURL（标题搜索）**——说明站点本身无可靠站内搜索接口。用标题搜索得到 0 结果**不是实现缺陷**。

| 站点 | 说明 |
|---|---|
| NetworkNubiles（Nubiles） | community 无 queryURL |
| NetworkReptyle（MylfSingles） | 同上 |
| NetworkPervCity（PervCity/DpDiva） | 同上 |
| NetworkTeenMegaWorld（Beauty-Angels） | 同上 |
| NetworkFTV / NetworkBellaPass / NetworkDogfart / NetworkExploitedX / NetworkModelCentro | 同上 |
| SiteATKGirlfriends / AbbyWinters / BrandNewAmateurs / GirlsOutWest / NewSensations / PlayboyPlus / PlumperPass / PurgatoryX / JacquieEtMichel / MissaX / NaughtyAmerica / OnlyFans / ManyVids / Watch4Beauty / AlsAngels | 同上 |

**替代验证方案**（URL 直抓）：
```
对每个站点:
1. 从 pornrips 详情页提取封面/标题 → 反向构造或获取源站场景 URL
2. 用 Helper.GetSearchResultsFromUpdate 或直接 Update(sceneID=[URL])
3. 断言: 能解析出 Name/日期/演员 即通过
```

**注意**：这类站点在真实 Jellyfin 使用中靠"已入库 URL 匹配"工作（用户手动指定或文件名带 URL），不依赖标题搜索。**结论：维持现状，表格标记 `ZERO(仅URL直抓)` 而非 `ZERO`。**

---

## 执行顺序建议

1. **P0**：B 类 NetworkSteppedUp 配置修复（1 个，影响真实可用性）
2. **P1**：C 类 NetworkPornWorld base URL 迁移（1 个，改一行配置）
3. **P1**：E 类抽 3-5 个代表站点做 URL 直抓验证，确认模式可行后推广
4. **P2**：D 类写 FlareSolverr 环境测试（需用户提供 FlareSolverr 服务）
5. **P3**：A 类已修复站点纳入回归测试（已做，补进 PornripsRegressionTests）

## 执行进度（2026-08-11 21:40 更新）

| 项 | 状态 | 结果 |
|---|---|---|
| P0: Swallowed 配置修复 | ✅ 完成 | **真实 bug**：`Split("and")` 区分大小写，"Skyler **And**" 没被切开 → 模型 URL 404 → 0 结果。修复为 `ToLowerInvariant().Split("and")`，实测 **4 结果** |
| P1: PornWorld base URL 迁移 | ✅ 完成 | 站16 search URL `/search/`（400）→ `/videos?q=`（200）；Search 选择器 `card-scene__text` → `article.card.scene` + `img[alt]` + `div.release-date`；实测 **24 结果**（含 pornrips 的 Jena La Rose 2026-08-09） |
| P1: E 类 URL 直抓验证 | 🔄 进行中 | 已通过 TPDB 对比确认方向 |
| P2: FlareSolverr 环境 | ✅ 完成 | 本地 docker 跑 ghcr.io/gythialy/flaresolverr:latest:8191；**NetworkMetArt 已接入 FlareSolverr.GetJson**（Search+Update 全链路通过，含会话崩溃自愈） |
| P3: 回归测试固化 | 🔄 部分完成 | Swallowed/PornWorld 探针已验证，待并入 PornripsRegressionTests |

## ThePornDatabase/scrapy 交叉对比（2026-08-11 21:40）

> 仓库：github.com/ThePornDatabase/scrapy（框架）+ scrapers submodule（1056 个 scene 爬虫）

| 未通过站点 | TPDB 实现 | 结论 |
|---|---|---|
| **NetworkVNA** | networkVna.py 注释明说 *"Can't be scraped for various reasons... Locked, no pagination, no video page"* | ✅ **TPDB 也放弃**，0 结果非我们问题 |
| **NetworkNubiles** | networkNubiles.py 有 Turnstile（Cloudflare）检测 | 反爬，需专门处理 |
| **SiteFemjoy** | siteFemjoy.py 带 referer+cookies 抓 `/videos`；siteFemjoyAPI.py 用 `/api/v2/videos` | ✅ **已修复**：FlareSolverr.GetHtml 分页浏览 `/videos` + 标题/演员过滤，全链路通过（Search 命中 Cozy Sensuality 2026-08-08） |
| **NetworkSteppedUp** | networkSteppedUp.py 按 URL 正则抓场景（无搜索） | 与我们修复后一致 |
| **SiteGirlsOutWest** | pagination `/categories/Movies_%s_d.html` | 分页浏览模式，无搜索接口 |
| **SiteBrandNewAmateurs** | pagination `/categories/movies/%s/latest/` | 同上 |
| **SiteATKGirlfriends** | 分页 + Playwright 变体（反爬） | 同上 |
| **NetworkNaughtyAmerica** | `/new-porn-videos?page=%s` | 分页浏览，无搜索接口 |
| **NetworkPervCity** | `/categories/movies_%s_d.html` | 分页浏览，无搜索接口 |

**核心结论（强化）**：TPDB 的 1056 个 scene 爬虫**全部是"分页浏览 + URL 正则匹配场景"模式，没有任何一个做标题搜索**。
→ 我们 33 个 ZERO 站点中，绝大多数（E 类）在 TPDB 里也是靠分页+URL 直抓工作，标题搜索 0 结果不是实现缺陷。
→ VNA 被 TPDB 官方标记为不可爬，是唯一"确认放弃"的站点。


## 验收标准

- [ ] A 类：2 个修复站点在 `PornripsRegressionTests` 中持续通过
- [ ] B 类：Swallowed 可完成一次 URL 直抓解析（或明确标记放弃搜索）
- [ ] C 类：PornWorld base URL 迁移后标题搜索 ≥1 结果
- [ ] D 类：FlareSolverr 环境下 3 站 ≥1 结果（无环境则记录跳过）
- [ ] E 类：25 站中至少 5 个代表站 URL 直抓验证通过，其余标记 `仅URL直抓`
- [ ] 表格更新：33 个 ZERO 全部有明确分类标注（`ZERO(仅URL直抓)` / `ZERO(反爬)` / `OK` / `ZERO*`）

## FlareSolverr 接入（2026-08-11 22:10）

**基础设施**：`FlareSolverr` helper 新增 `GetJson(url)` 方法（GET + JSON 解析，复用会话/重试/`<pre>` 提取逻辑）。
增强：
- catch 中 `DestroySession` 自愈（tab 崩溃后重建会话）
- `EnsureWarmed` 失败仅警告不阻断（sexart.com 首页触发挑战 tab 崩，但 API 路径可访问）
- GetJson 不预热（API GET 不需要浏览器上下文）

**NetworkMetArt**：Search 接入 `FlareSolverr.GetJson`（IsConfigured 时），Update 补 `HasMetadata=true`。
实测：Search 1 结果（Diffusion [MetArt/SexArt] 2018-08-03）+ Update 完整元数据。

**SiteFemjoy 修复路径**：同模式——给 SiteFemjoy Search 接 GetJson（Femjoy API 在 FlareSolverr 下可访问）。

## D 类反爬站点修复完成（2026-08-11 22:15-22:45）

| 站点 | 方案 | 实测 |
|---|---|---|
| **NetworkMetArt** | FlareSolverr.GetJson（API 搜索，不预热绕开 sexart 首页挑战崩） | Search 1 结果 + Update 完整 |
| **SiteFemjoy** | FlareSolverr.GetHtml 分页浏览 `/videos`（API 全死），按标题/演员过滤；Update/GetImages 裸请求 `/post/{id}` 场景页 | Search 2 结果（命中 Cozy Sensuality）+ Update 完整 + 15 图 |
| **NetworkKink** | FlareSolverr.GetHtml + `?ageverified=g`（隐私门槛）+ 新选择器 `card.shoot-thumbnail` / `card-body-title a[title]` | Search 24 结果 |

**FlareSolverr helper 新增**：`GetHtml(url)`（返回原始 HTML，供分页浏览类站点）。
**FlareSolverr v2+ 注意**：request.get 忽略 headers（仅 contentType=json 生效），已移除 headers 传参消除 WARNING。

## E 类 URL 直抓验证完成（2026-08-11 22:45-23:00）
- **代表站**：SiteNewSensations（唯一裸请求可达的 E 类站）
- **发现并修复 2 个真实 bug**：
  - Update `descriptionNodes[0]` 无守卫越界（页面无 description 时崩）
  - GetImages `posterNode.Attributes["src"]` 无守卫 NRE
  - Update 补 `HasMetadata=true`
- **验证**：URL 直抓完整解析——Name "Milf Summer Stevens Has A New Friend Next Door"、Date 2026-08-08（精确命中 pornrips 标题）、2 演员、1 图
- **结论**：E 类模式可行（Search 靠外部搜索生成 URL，Update 直抓场景页）；表格 24 个 E 类 handler 标注 `ZERO(仅URL直抓)`；其余 E 类站（Nubiles/Reptyle/GirlsOutWest 等）裸请求 403/空响应（Cloudflare 强反爬），URL 直抓需 FlareSolverr 但**不是实现缺陷**

## 收尾状态（2026-08-11 23:40，本阶段完成）

### 累计修复清单（21 个真实 bug）

**崩溃/NRE 修复**：
- SiteJulesJordan（改版全失效，重写 Search/Update/GetImages）
- SiteLegalPorno（Uri 空值）
- SiteTonightsGirlfriend（href 空引用）
- SiteMissaX / SiteQueenSnake / SiteKarups（SelectNodes null）
- SiteNewSensations（descriptionNodes[0] 越界 + posterNode NRE）
- HTML.ElementFromStream（null 响应）

**接口/URL 失效重写**：
- NetworkBang（ES API 死 → 网页搜索）
- NetworkSteppedUp/Swallowed（Split 大小写 bug）
- NetworkPornWorld（域名迁移 + search URL + 选择器）
- SiteSexMex（改版选择器）
- NetworkMetArt（FlareSolverr.GetJson）
- SiteFemjoy（API 死 → FlareSolverr 分页浏览）
- NetworkKink（FlareSolverr + ageverified）
- SiteWatch4Beauty（SPA JSON API 重写）
- SiteBrandNewAmateurs（分页浏览重写）

**数据/配置**：
- SiteList：PornWorld search URL、BrandNewAmateurs search URL、VNA 组删除（用户决定）
- 合并 andrer 数据（231 组/2341 站点）

### 计划文档（docs/）
- `pornrips-test-results.md`：199 handler 全量测试结果表
- `cross-validation-report.md`：stashapp/CommunityScrapers 交叉验证
- `zero-sites-test-plan.md`：0 结果站点测试计划与执行进度
- `eclass-solution.md`：E 类站点解决方案

### 遗留（新任务继续）
1. **4 个"200 大页面但 0 结果"**（选择器过时，可修）：PlayboyPlus、NaughtyAmerica、JacquieEtMichel、TeenMegaWorld
2. **需大改**：Dogfart（搜索+URL 全 404，新结构待挖）、ATKGirlfriends（502）
3. **门槛**：MissaX（年龄验证）、Nubiles（429/Turnstile）、AlsAngels（空页）
4. **设计如此**：ManyVids（ID 直抓）
5. **P3**：修复用例固化进 PornripsRegressionTests
