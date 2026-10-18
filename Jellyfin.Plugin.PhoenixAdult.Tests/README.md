# PhoenixAdult 测试

xUnit 测试项目，分三类：

## 1. 离线数据测试（默认，无需网络）

CI 默认执行，覆盖数据完整性与用例目录一致性：

- `SiteListDataTests`：验证 `data/SiteList.json` 与 Sites 实现类的映射关系。
- `ProviderRegressionCatalogTests`：验证统一回归用例目录 `ProviderCases.json`
  （ID 唯一、字段完整、handler 有实现类、组/站点存在于 SiteList、组与 SiteIDList 一致）。

```bash
# 本地
dotnet test Jellyfin.Plugin.PhoenixAdult.Tests --filter "Category!=Online"

# docker
docker build -t phoenixadult-tests -f Dockerfile.test .
docker run --rm phoenixadult-tests
```

## 2. Provider 统一回归（`Category=Online`，需要网络）

历史上的分散探针测试（`*ProbeTests`、`Pornrips*`、`P1PaginateTests`、
`VerifiedScenesRegressionTests` 等）已归并为**一份数据目录**：

> `ProviderRegression/ProviderCases.json`

由数据驱动逐条执行，每条用例在测试结果中独立呈现、独立通过/失败，无需为新增用例改 C# 代码。

| kind | 执行路径 | 用例数 | 说明 |
|---|---|---|---|
| `search` | `provider.Search` | 62 | 有序列多级搜索词回退（命中即停）；标记 `chain` 的用例在命中后继续 `Update`→`GetImages` 全链路 |
| `update` | `provider.Update(场景 URL)` | 4 | URL 直抓解析回归（站点改版保护） |
| `e2e` | `MovieProvider` `Search`→`GetMetadata` | 12 | 真实集成路径全链路，断言标题/日期/演员 |
| `images` | `MovieImageProvider.GetImages` | 0（保留） | 需要确定性 sceneId；当前由 search 的 `chain` 覆盖同一路径 |

> search 用例分两档语义（与历史测试一致）：**严格档**（已验证站点/探针/分页，要求 ≥1 结果）
> 与 **批量探测档**（`allowZero=true`，0 结果只记 ⚠️ 不失败，保护“不崩溃”）。

### 图片“真实可下载”校验

`chain` / `expectImages` 用例在搜索命中后，会经 `MovieImageProvider.GetImages` 取图，
再复跑插件内部的 `ImageHelper.GetImagesSizeAndValidate`——即**与 Jellyfin 自动下载完全相同的路径**：
HEAD 可达 → 拉取字节 → 解析尺寸（>100px）→ backdrop 宽 1280 过滤。
断言 Primary ≥1 张且有可解析尺寸，等价于“在 Jellyfin 里能下载到正确图片”。

未覆盖的 handler 清单见 [provider-regression-uncovered.md](../docs/provider-regression-uncovered.md)。

```bash
# 全部在线回归
dotnet test Jellyfin.Plugin.PhoenixAdult.Tests -c Release \
  --filter "Category=Online&FullyQualifiedName~ProviderRegression"

# 按类型拆分
dotnet test ... --filter "FullyQualifiedName~ProviderRegressionSearchTests"
dotnet test ... --filter "FullyQualifiedName~ProviderRegressionUpdateTests"
dotnet test ... --filter "FullyQualifiedName~ProviderRegressionMovieTests"
```

- 单条用例在结果里显示为 `用例 ID [kind] 站点名`（例如 `search-sexmex-199-0 [Search] SexMex`）。
- 环境依赖（FlareSolverr 未配置 / 8191 端口拒绝）自动识别为 SKIP，不计失败。
- 汇总报告写入 `bin/<Config>/net10.0/provider-regression-report.txt`。

### 如何新增 / 修改用例

编辑 `ProviderRegression/ProviderCases.json` 的 `cases` 数组即可（改完重新 build/run）：

```json
{
  "id": "search-mysite-100-0",
  "kind": "search",
  "handler": "NetworkXxx",
  "group": 100,
  "site": 0,
  "siteName": "MySite",
  "searchTerms": ["Actor Name Scene Title", "Actor Name"],
  "expectMinResults": 1,
  "note": "来源/说明"
}
```

字段速查：

| 字段 | 适用 kind | 含义 |
|---|---|---|
| `id` | 全部 | 稳定唯一 ID |
| `handler`/`group`/`site`/`siteName` | search/update | SiteIDList handler 与站点坐标 |
| `searchTerms` | search | 有序搜索词，返回 0 结果时按序回退 |
| `expectMinResults` | search | 最少结果数（默认 1） |
| `allowZero` | search/update | 允许 0 结果：不崩溃即通过（`expectMinResults=0` 时必须为 true） |
| `expectedFirstContains` | search | 首结果标题需包含的片段（硬断言） |
| `expectedFirstHint` | search（可选） | 历史观察到的首结果标题（仅记录，不断言） |
| `chain` | search | 命中后继续 Update/GetImages |
| `expectName`/`expectMinActors`/`expectImages`/`expectPremiereDate` | search+chain / update | 全链路与直抓断言 |
| `sceneUrl` | update | 场景 URL（相对或绝对） |
| `fileName`/`expectDate`/`expectTitleContains`/`expectActors` | e2e | MovieProvider 全链路输入与断言 |
| `rssTitle`/`note`/`source` | 全部（可选） | 溯源信息 |

用例目录会被 `ProviderRegressionCatalogTests` 离线校验，站点删除/组号变更会立即报错。

## 3. 全量在线冒烟（`Category=Online`，手动）

`SiteSmokeTests`：对 SiteIDList 中每个 handler 实例化并执行一次真实 Search，
验证站点页面可访问、解析器正常工作。并发 8，单请求 45s 超时，结果写入 `smoke-report.txt`。

```bash
docker run --rm phoenixadult-tests \
  dotnet vstest Jellyfin.Plugin.PhoenixAdult.Tests.dll --TestCaseFilter:"Category=Online"
```

## 路径解析

测试通过 `PHOENIX_DATA_DIR` / `PHOENIX_REPO_ROOT` 环境变量定位 `data/` 目录
（docker 内已设置），本地开发环境自动按仓库结构推断。
