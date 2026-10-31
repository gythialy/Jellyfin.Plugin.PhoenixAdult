# JavDB 源：能力、配置与排障

JavDB（`https://javdb.com`）在本插件里是一路 **JAV 番号元数据 / 封面 / 演员头像** 来源，
实现见 `Jellyfin.Plugin.PhoenixAdult/Sites/SiteJavDB.cs`（组号 255，`SiteList.json` 里的 `JavDB`）。

## 1. 能做什么

| 能力 | 是否需要登录 | 说明 |
|---|---|---|
| 番号搜索 | 否 | `GET /search?q=<番号>&f=all`，只认结果卡片 `<strong>` 里**同号**的条目（同号多条取第一条） |
| 有码作品详情 | 否 | `GET /v/<id>?locale=zh`：标题、发行日期、片长、导演、片商、发行、系列、类别、演员、封面 |
| 无码作品详情 | **是** | 无码作品页会 302 到 `/login`；未配置 Cookie 时该条目按“未取得”降级（返回空结果，不抛异常） |
| 封面 / 样图 | 否 | `c0.jdbstatic.com` 公开 CDN，无签名、无 Referer 校验；封面作 Primary，`_l_` 全尺寸样图作 Backdrop（上限 30 张） |
| 演员头像 | 否（列表页） | `/search?f=actor&q=<演员名>` 卡片里的 `img.avatar`，或按 `avatars/<id 前两位小写>/<id>.jpg` 规律取；`actor_unknow.jpg` 是占位图，会被过滤 |
| 演员页大图 | **是** | 配置了 Cookie 时优先读 `/actors/<id>` 页内头像 |

番号归属：`data/IdRoutes.json` 的 `jav` 路由里包含 `[255, 0]`，所以**标准 JAV 番号会与 JavBus / AVBase /
JAVDatabase / FANZA 等源并发检索**；用户显式写站点名（如 `JavBus ABP-123`）时保持单站行为。

> JavDB 并不收录所有厂牌。例如 `HEYZO-0929` 在 JavDB 查不到，但 JavBus 的无码区有完整数据——
> 这类番号靠多源检索互相兜底，不需要额外配置。

## 2. 配置

两个配置项都在 **Dashboard → Plugins → PhoenixAdult → JavDB** 小节（配置页支持中英双语，
按 Jellyfin 用户文化 / 浏览器语言自动切换）。

### 2.1 JavDB Cookie（可选）

**用途**：只用于「演员页大图」和「无码作品详情」。不填时，这两类页面被跳过；有码作品、封面、样图、
演员搜索结果页完全不受影响。

**取值步骤**：

1. 在桌面浏览器登录 `javdb.com`，打开任意演员页或无码作品页；
2. 按 `F12` → **Network / 网络**，刷新页面，选中类型为 `document` 的 HTML 请求；
3. 在 **Request Headers / 请求头** 里找到 `Cookie:`，复制它后面的整段值；
4. 粘贴到插件的 `JavDB Cookie` 输入框并保存。

**示例**（输入框的占位提示就是这种形态，实际值更长）：

```
over18=1; _jdb_session=xxx…xxx; locale=zh
```

要点：

- 不要加引号，分号要保留，字段之间是 `名字=值`；
- 只需要 `javdb.com` 域下的 Cookie；
- 登录态会在 JavDB 侧过期（通常数周）。演员页或无码作品又开始解析不到时，重新贴一次即可；
- 该值以**明文**保存在本服务器的插件配置里，请当作凭据对待；不再需要时把输入框清空并保存。

### 2.2 Cookie 能自动续期吗

**不能。** JavDB 的登录表单需要三样东西：`authenticity_token`（CSRF，随会话变化）、账号密码、以及
**图形验证码 `_rucaptcha`**（`/rucaptcha/?t=…` 那张图）。验证码决定了插件无法在后台自动登录，也就无法
自动续期 Cookie；FlareSolverr 只能过 Cloudflare 挑战，解不了图形验证码。

现实做法是：

- 登录态通常能撑数周，过期后重新贴一次（约 1 分钟）；
- 插件会在失效时给出可检索的信号：日志里出现
  `[JavDB] The configured JavDB cookie no longer passes the login wall (expired or revoked)`；
  没配置过 Cookie 时则是
  `[JavDB] This page requires a JavDB account. Paste a session cookie in the plugin settings…`；
- 有码作品的元数据、封面、样图、以及演员搜索结果页**完全不依赖** Cookie，所以即使 Cookie 过期，
  日常刮削也不会中断，只是演员页大图和无码作品暂时取不到。

**自检入口**：配置页 JavDB 小节里的「检测 Cookie」按钮（`POST /PhoenixAdult/javdb/cookie-test`，
仅管理员可调）。它会把输入框里**当前**（包括还没保存）的值发给插件后端，访问两个受登录保护的入口
（`/users/profile` 与无码演员页）再给出结论：

| 结果 | 含义 | 处理 |
|---|---|---|
| `valid` | Cookie 能过登录墙 | 保存即可 |
| `expired` | 过期或被注销 | 重新登录 javdb 取新值 |
| `missing` | 输入框为空且配置里也没存 | 先填值 |
| `invalid` | 格式不是 `名字=值; 名字=值` | 检查粘贴内容（别带引号） |
| `unknown` | 资料页能过但受保护页面不像演员页 | JavDB 可能改版，看日志 |
| `error` | 网络/超时/被拦 | 检查代理与 FlareSolverr |

### 2.3 演员没有头像时用作品封面兜底（默认开启）

不少日本女优在任何公开数据库里都没有头像（Gfriends 无收录、AV-LEAGUE 无条目、JavDB 头像 CDN 也是 404）。
开启时：所有头像源都为空（或返回的图校验不通过）后，取她在 JavDB 某部作品的封面当人物图，来源标注
`JavDB (cover)`，方便和真正的头像区分。

关掉之后：这些演员就是「没有照片」，不会再用横版封面顶替。

## 3. 失败语义与降级

| 场景 | 行为 |
|---|---|
| 搜索无同号结果 | 返回空（`not_found`），不把近似项当命中 |
| 无码作品页 302 → `/login` | 记一条 Info 日志，`Update` 返回空结果，条目保持未识别状态 |
| 站点返回 JSON/HTML 异动 | 解析失败只跳过该字段；没有任何可用字段时不写入库 |
| 被 Cloudflare 挑战 | 正常页面不会触发挑战；只有 `<title>` 命中 `Just a moment…` / `请稍候…` 时才交给 FlareSolverr 兜底（见 `docs/scraper-lessons.md` 的 Referer/挑战判定说明） |
| 图片 403/404 | 交由 `ImageHelper` 的统一校验丢弃，不会把破图写进库 |

## 4. 回归用例

`Jellyfin.Plugin.PhoenixAdult.Tests/ProviderRegression/ProviderCases.json`：

| 用例 | 断言 |
|---|---|
| `search-javdb-ssis-354` | 有码番号搜索命中 + Update 有标题/演员/首播日期 + GetImages 至少 1 张 |
| `e2e-javdb-ssis-354` | 经 `MovieProvider` 全链路（多源合并后首个来源是 JavBus，日期按合并结果断言） |

跑法见 [`Jellyfin.Plugin.PhoenixAdult.Tests/README.md`](../Jellyfin.Plugin.PhoenixAdult.Tests/README.md)。

## 5. 排障

- **有码条目正常、无码条目识别不到** → 正常，需要配置 `JavDB Cookie`。
- **演员照片变成一张横版封面** → 该演员没有公开头像，命中封面兜底；不想要就关掉开关。
- **演员识别不出照片也没有封面** → 说明 JavDB 搜索也没命中她的作品（多见于刚出道或别名不同），
  可在 `data/Actors.local.json` 里补别名后再试。
- **配置保存后要重启吗** → 不需要，下一次抓取即生效；插件配置由 Jellyfin 持久化，无需改任何文件。
