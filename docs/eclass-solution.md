# E 类站点（ZERO(仅URL直抓)）解决方案

> 依据：24 个 handler 的 Search 实现策略分析 + Jellyfin 真实调用链（文件名识别 → GetSiteFromTitle → Search → Update）。
> 核心：这些站**无可靠站内标题搜索**，Jellyfin 里靠「文件名含站点名→识别站点→Search 返回候选→用户确认→Update 抓元数据」工作。
> 日期：2026-08-11

## Jellyfin 调用链（现状）

```
文件: /Movies/NetworkNubiles.26.08.10.Maia.Spir.Make.Me.Sweat.XXX.mkv
  → MovieProvider.Search:
    1. GetSiteFromTitle(文件名) → 识别站点 (siteNum[0]:siteNum[1])
    2. GetClearTitle → 去掉站点名 → "Maia Spir Make Me Sweat"
    3. provider.Search(siteNum, 场景名) → 期望返回候选
    4. 用户选中 → Update(sceneID) 抓元数据
```

## 问题根因

1. **Search 依赖外部搜索（Google/DuckDuckGo）**：9 个 handler 用 WebSearch/GoogleSearch，测试环境和部分用户环境 Google 反爬 → 0 结果
2. **年龄/隐私门槛 + Cloudflare**：MissaX/NaughtyAmerica 等裸请求返回 Age Verification 页 → Search 解析 0 结果
3. **无站内搜索接口**：Nubiles/Reptyle 等只有分页浏览 → 标题搜索天然 0 结果
4. **URL slug 构造脆弱**：场景名→URL 需精确匹配，改版即失效

## 分层解决方案

### 方案 A：直接 URL 构造 + GetSearchResultsFromUpdate（5 个，改动最小）

适合 B 类（场景名→slug 可预测）。**用文件名场景名直接构造场景 URL，跳过 Search**：
```csharp
// 示例：SiteBrandNewAmateurs 改进
public async Task<List<RemoteSearchResult>> Search(...)
{
    // 场景名 → slug URL → 直接 Update 抓取
    var slug = searchTitle.Replace(' ', string.Empty).ToLower();
    var sceneURL = $"{Helper.GetSearchSearchURL(siteNum)}/{slug}.html";
    var sceneID = new[] { Helper.Encode(sceneURL) };
    return await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID, searchDate, cancellationToken);
}
```
站点：SiteBrandNewAmateurs、NetworkDogfart、SiteGirlsOutWest、SitePlumperPass、SiteWatch4Beauty

### 方案 B：数字 ID 优先 + URL 直抓（5 个，改动小）

适合 C 类（已有 ID 直抓逻辑）。**强化 ID 路径 + 失败时提示**：
- NetworkModelCentro / NetworkNubiles / SiteJacquieEtMichel / SiteManyVids / NetworkFTV
- 已支持 `searchTitle[0]` 为数字 ID → 直抓场景；保留但补 null 守卫

### 方案 C：FlareSolverr 绕过门槛（6 个，依赖 FlareSolverr 环境）

适合反爬站点，复用已修的 MetArt/Femjoy/Kink 模式（GetHtml + 特定参数）：
- SiteMissaX（age verification → 需 cookie/参数）
- SiteNaughtyAmerica（Cloudflare）
- SiteAlsAngels（Cloudflare）
- SiteATKGirlfriends（分页 + Turnstile）
- NetworkNubiles（Turnstile，需专门处理）
- NetworkTeenMegaWorld（分页）

### 方案 D：外部搜索降级 + 缓存（9 个，治本）

适合 A 类（依赖 WebSearch）。**Google 反爬是环境问题**，升级为：
1. 优先站内搜索（BellaPass/Reptyle/PervCity 有站内 search URL）
2. WebSearch 失败时降级为「分页浏览 + 场景名过滤」（如 Femjoy 模式）
3. 结果缓存：同一文件名二次刮削直接命中

## 优先级建议

| 优先级 | 方案 | 站点数 | 工作量 | 效果 |
|---|---|---|---|---|
| P0 | A：URL 直接构造 | 5 | 小 | 立即可用（无需外搜/反爬） |
| P1 | B：ID 直抓强化 | 5 | 小 | 文件名带 ID 时可用 |
| P2 | C：FlareSolverr | 6 | 中 | 需 FlareSolverr 环境 |
| P3 | D：搜索降级+缓存 | 9 | 大 | 治本，覆盖 A 类 |

## 验收标准

- [ ] P0 5 站：真实文件名 Search 返回 ≥1 候选（裸请求可达的站）
- [ ] P1 5 站：ID 文件名 Search 直抓成功
- [ ] P2 6 站：FlareSolverr 环境下 Search 返回结果
- [ ] P3 9 站：无外搜依赖，站内/降级路径可用
- [ ] 表格标注更新：`仅URL直抓` → 实际结果
## 实测补充（2026-08-11 23:00）

验证了 3 个 B 类站的真实可达性，发现**并非全部是"仅URL直抓"——部分是可修复的配置/解析过时**：

| 站点 | 实测 | 结论 |
|---|---|---|
| **SiteWatch4Beauty** | search URL `/models/search?q=` → **404**（应为 `/search?q=`）；且搜索结果是**内嵌 JSON**（`model_nickname`/`model_simple_nickname`），非 HTML | ❌ **实现过时**：URL 错 + 解析方式错，**可修复** |
| **SiteMissaX** | `/tour/search.php?query=` → 200 但返回 **Age Verification 页**（9KB） | ⚠️ 年龄门槛，需 cookie/FlareSolverr |
| **SiteBrandNewAmateurs** | 裸请求首页 200 | ✅ 可达，URL 直抓可行 |

**结论修正**：24 个 E 类站里至少 1 个（Watch4Beauty）是**可修复的真实 bug**（不是"仅URL直抓"），说明 E 类标注可能低估了问题——**逐个验证仍必要**，优先级上调。

## 推荐执行顺序（修订）
1. **P0**：SiteWatch4Beauty 修复（已定位：URL `/search?q=` + JSON 解析）——证明"仅URL直抓"站里混着可修 bug
2. **P1**：方案 A（5 站 URL 直接构造）+ 方案 B（5 站 ID 直抓）落地
3. **P2**：方案 C（6 站 FlareSolverr）
4. **P3**：方案 D（9 站外搜降级）
5. 每修一个站更新表格标注（`仅URL直抓` → 实际结果）

## 方案 A/B 批量验证结果（2026-08-11 23:40-23:55）

用 pornrips 真实标题批量测 12 个站：**OK 1（ModelCentro 327 结果）/ ZERO 11 / CRASH 0**。

| 站点 | 实测 | 分类 |
|---|---|---|
| **NetworkModelCentro** | ✅ 327 结果（ID 直抓，返回全部场景） | C 类正常 |
| **SiteBrandNewAmateurs** | ✅ **已修复**：search URL `/models`→`/categories/movies/`，Search 改分页浏览+标题过滤 | A/B 可修 ✅ |
| **NetworkDogfart** | 搜索 404 + tour URL 全 404 | 需大改（新 URL 结构） |
| **SiteMissaX** | 200 但返回 Age Verification 页（9KB） | 年龄门槛 |
| **SitePlayboyPlus** | 200 大页面但 0 结果 | 选择器或 URL 过时 |
| **SiteNaughtyAmerica** | 200 459KB 但 0 结果 | 选择器过时（大页面有内容） |
| **SiteAlsAngels** | 200 但 4.6KB 太小 | 门槛/空页 |
| **NetworkNubiles** | 429 限流 | Cloudflare 限流 |
| **SiteJacquieEtMichel** | 200 380KB 但 0 结果 | 选择器过时（大页面有内容） |
| **SiteManyVids** | ID 直抓设计（pornrips 标题无 ID → 0） | 设计如此 |
| **NetworkTeenMegaWorld** | 200 95KB 但 0 结果 | 选择器或 URL 过时 |
| **SiteATKGirlfriends** | 502 | 服务端问题 |

**结论**：E 类里**至少 4 个是"页面有内容但解析过时"**（PlayboyPlus/NaughtyAmerica/JacquieEtMichel/TeenMegaWorld——都是 200 大页面），修复价值高；2 个需大改（Dogfart/ATKGirlfriends）；2 个年龄/限流门槛（MissaX/Nubiles）；1 个设计如此（ManyVids）。

**推荐下一步**：优先修 4 个"200 大页面但 0 结果"（选择器过时，改动小收益高）。
