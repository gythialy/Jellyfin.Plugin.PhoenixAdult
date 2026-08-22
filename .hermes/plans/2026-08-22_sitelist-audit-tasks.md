# SiteList.json 全量审计任务规划

> **For Hermes:** 按任务逐项执行；每个站点修复沿用已沉淀的验证方法论（解析链路分析 → curl 抓真实页面 → 临时 xUnit 全链路 → 双平台构建）。

**Goal:** 基于 TPDB（1055 scrapers）+ stash CommunityScrapers（641 yml）参考实现与全量域名存活探测，修复 SiteList.json 中已失效的注册项，并按优先级列出新站接入计划。

**审计方法：**
1. 结构校验：SiteIDList ↔ Sites/*.cs ↔ Sites 组引用三向一致（✅ 全部通过，无死引用/孤儿组）
2. 域名存活：834 个唯一 base 域名并发 GET 探测（UA=Chrome，http 兜底），403/429 与 000 分开判定
3. 参考比对：TPDB/stash 站点级 scraper 的主域集合 vs 我们的注册域集合求差
4. 误报复核：批量探测的 503 用 curl 单点复测（TMW 全系为限流误报）

---

## 审计结论总览

| 维度 | 结果 |
|---|---|
| 组/子站/handler 结构 | ✅ 221 组 / 2290 子站，无死引用、无孤儿 |
| 唯一 base 域名 | 834 个探测，**确死 5 域** + **路径过期 1 处** + **PornPortal 频道 404×10** |
| 反爬 429/403 | 55 域（Nubiles 全网 46 域限流为主），插件走 API/直连页面不受影响，暂不处理 |
| 新站候选 | TPDB/stash 有站点级 scraper 而我们未注册的约 938 个（过滤聚合器/CDN 后），精选高价值 30 个 |

---

## A. 修复任务（有问题）

### Task A1: Intersec 全系域名失效（组 95，10 个子站）
- **现象**: `www.insexondemand.com` https/http 均 000（连接失败），组 95 全部子站挂
- **参考**: TPDB `networkInsexSites.py` / stash `insex.yml` 用各站独立域名（hardtied.com / sexuallybroken.com / infernalrestraints.com / realtimebondage.com / topgrl.com 等）
- **动作**: curl 逐个验证独立域名存活 → 存活则改注册为独立域名（每站 data[1]），全死则考虑 TPDB 的 insex.com 新入口
- **文件**: `data/SiteList.json` 组 95 #0-#9
- **验证**: ad-hoc xUnit 抽 2 站（Hardtied + Sexuallybroken）全链路

### Task A2: PornPortal 频道子域全灭（组 47 #1-11 + 组 63 #221-232，双注册）
- **现象**: 10 个 `*.pornportal.com` 频道子域 404（3dxstar/ebony/latina/teen/milf/stepfamily/lesbian/realitygang/anal/bbw）
- **判断**: project1service 把频道并入主站（pornportal.com 主域 200）；频道内容大概率走主站搜索 + site 过滤
- **动作**: 验证主站 API 能否带频道过滤 → 能则改注册指向主域；不能则删组 63 重复注册（组 47 保留）
- **注意**: Network1service handler 是通用 API，改动风险低；两组完全重复是历史遗留
- **文件**: `data/SiteList.json` 组 47 / 组 63
- **验证**: ad-hoc xUnit 任一频道场景全链路

### Task A3: Exposed Whores 搜索路径过期（组 176 #2，SiteMissaX）
- **现象**: 注册 `/new-tour` 路径 404，裸域 exposedwhores.com 200（elxcomplete 平台）
- **参考**: stash `reidmylips.elxcomplete.com` 同平台用根路径 search.php
- **动作**: curl 找新搜索路径（大概率去掉 new-tour 前缀）→ 更新 data[2]
- **文件**: `data/SiteList.json` 组 176 #2
- **验证**: ad-hoc xUnit 全链路

### Task A4: BellaPass Cali Carter 域名死（组 75 #4）
- **现象**: www.calicarter.com 连接失败；TPDB networkBellaPass.py 仍引用 calicarter.com（参考也旧）
- **动作**: 试裸域 calicarter.com / web.archive.org 判断站点是否整体关停 → 关停则删条目（NetworkBellaPass 其他 17 站正常保留）
- **文件**: `data/SiteList.json` 组 75 #4
- **验证**: 构建通过即可（删条目无代码依赖）

### Task A5: VRPFilms / HoloGirlsVR / AussieAss 孤站死亡评估
- **现象**: vrpfilms.com（组220）、www.hologirlsvr.com（组158）、www.aussieass.com（组132）均连接失败，各自单站成组
- **参考**: TPDB 无这三站的活跃 scraper
- **动作**: 各自确认是否迁移（VRPFilms→查 vrpfilms.net 等变体；AussieAss 查 aussieass.com.au）→ 迁移则改 URL，关停则删组+SiteIDList 条目+handler 文件（GetProviderBySiteID 反射机制保证删后编译安全）
- **文件**: `data/SiteList.json` + 可能删 `Sites/SiteVRPFilms.cs` 等
- **验证**: 双平台构建 0/0

### Task A6: HeavyOnHotties 重定向核实（组 156 #0）
- **现象**: python 探测 500 但 curl http1.1 301——疑似 http/2 或路径敏感
- **动作**: curl -L 跟随看最终落地域 → 若迁移到新域名更新注册；若同域偶发 500 则标记观察不动
- **文件**: 视结果 `data/SiteList.json` 组 156

---

## B. 新增任务（TPDB/stash 支持而我们没有）

> 精选标准：欧美主流商业站、有活跃站点级 scraper、与现有 handler 模板（WordPress/project1service/API）同构概率高。完整 938 个候选清单在 `/tmp/missing_sites.json`，此处按价值排序分批。

### Task B1: 高价值单站批次一（成人主流，WordPress/tour 模板概率高）
| 站点 | 域名 | 参考 | 预期模板 |
|---|---|---|---|
| AbuseMe | abuseme.com | TPDB siteAbuseMe | WP tour（同 WowNetwork 族） |
| MomPOV | mompov.com | TPDB siteMomPOV | 定制（需单独实现） |
| NetGirl | netgirl.com | TPDB siteNetGirl | 定制 |
| HookupHotshot | hookuphotshot.com | TPDB siteHookupHotshot | ember/WP |
| PlumperPass | plumperpass.com | TPDB networkPlumperPass | tour PHP |
| Scoreland | scoreland.com | TPDB siteScoreland | 定制（大站） |
| Zishy | zishy.com | TPDB siteZishy | 定制 |
| Cosmid | cosmid.net | TPDB siteCosmid | 定制 |

**动作**: 每站先 curl 探结构定模板归属 → 同构现有 handler 则纯 SiteList 注册，异构则新写 Site 类 → ad-hoc xUnit 验证 → 独立 hotfix PR

### Task B2: 高价值单站批次二（欧洲/艺术类，与 MetArt/Porndoe 相邻）
| 站点 | 域名 | 参考 | 备注 |
|---|---|---|---|
| MatureNL | mature.nl | TPDB siteMatureNL | 大站，API 可能 |
| FrolicMe | frolicme.com | TPDB siteFrolicMe | 艺术向 |
| Lustery | lustery.com | TPDB siteLustery | 真实情侣向 |
| GirlsOutWest | girlsoutwest.com | TPDB siteGirlsOutWest | 我们有 tour 引用但缺注册？核对 |
| PascalsSubSluts | pascalssubsluts.com | TPDB sitePascalsSubSluts | UK |
| PremiumBukkake | premiumbukkake.com | TPDB sitePremiumBukkake | |

### Task B3: 高价值单站批次三（gay 向，补全网络覆盖）
CockyBoys / HelixStudios / TimTales / MenAtPlay / LucasEntertainment / Freshmen — 均有成熟 TPDB scraper；用户需求导向，优先级可后调

### Task B4: Casting 类
CastingCouchHD / PornDudeCasting / TrueAmateurModels / PegasProductions（魁北克法语）/ LaFranceAPoil（法语）

### Task B5: 数据库/聚合类补充（低优先级）
AdultDvdMarketplace / HotMovies / AEBN VOD —— 非订阅站但 TPDB 支持，对"影片名刮削"场景有价值，视用户需要

---

## C. 明确不处理（记录原因）

- **Nubiles 全网 46 域 429**: 插件走 nubiles-porn.com API 直连，浏览器 UA 探测被限流不代表插件坏（VerifiedScenes 里 Nubiles 族用例绿即证明）
- **JAVLibrary/PerfectGonzo/Bellesa/Colette 403**: Cloudflare 挡脚本探测，插件有 FlareSolverr 通道，实际可用性以真实刮削为准
- **TMW 503 误报**: curl 单点全部 200，批量并发触发限流
- **938 完整候选清单**: 大量 gay/fetish/JAV/图库小站，等用户点名再逐个接

## 执行顺序建议

A1（Intersec 影响面最大，10 子站）→ A2（PornPortal 双注册清理）→ A3/A4/A5（小修）→ B1（首批新站）→ 其余按需。

每个 A 任务独立 hotfix PR（从 origin/master 拉 `hotfix/<name>` 分支）；B 任务每批次一个 PR。
