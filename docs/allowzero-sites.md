# allowZero 站点清单

本插件的在线回归里，配置了 allowZero（或用例备注写明"0 结果不失败"）表示：
**用例只验证搜索/元数据链路不抛异常、不崩溃，0 条结果不算失败。**

判定标准：某站点从当前网络**无法取到任何真实数据**（整站封锁、地域限制、维护、改版、域名失效），
且已用 curl / FlareSolverr 复刻确认是站点侧问题（不是实现 bug）。这类站点恢复后应把 allowZero 改回 false 转严格用例。

复验命令：

    dotnet test Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj -c Debug --filter "FullyQualifiedName~MetatubePortSmokeTests"
    dotnet test Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj -c Debug --filter "Category=Online&FullyQualifiedName~ProviderRegression"

单站复验：把 MetatubePortSmokeTests.cs 里对应行的最后一个参数 true 改成 false，跑单条通过即说明站点已恢复。

## 一、因站点不可达而暂时放宽的站点（换网络/等恢复后应转严格）

| 站点 | 组 | handler | 阻塞原因（实测） | 复验要点 |
|---|---|---|---|---|
| Cum4K | 21/66 | NetworkPornPros | CloudFront 403 并跳 /errors/forbidden，根域 301 到 /tours/1/；FlareSolverr 同出口也 403（地域/风控） | 美国出口下 www.cum4k.com 应为 200 |
| Faleno | 237 | NetworkFaleno | faleno.jp 返回 XSERVER 403（同组 dahlia 正常） | 日本出口验证 |
| MGS動画 | 243 | SiteMgsStage | mgstage.com CloudFront 403（WAF/地域） | 日本或干净出口 |
| SOD | 244 | SiteSOD | ec.sod.co.jp nginx 403（仅日本 IP） | 日本出口验证 |
| FC2Hub | 248 | SiteFC2Hub | Cloudflare 按 IP 封（FlareSolverr 报 IP is banned） | 换 IP 后应出结果 |
| FC2PPVDB | 249 | SiteFC2PPVDB | 同上 | 换 IP 后应出结果 |
| HeyDouga | 253 | SiteHeyDouga | 站点公告「緊急メンテナンス、復旧時期未定」 | 等站点恢复 |
| D2Pass 家族（c0930 / h0930 / h4610） | 254 | NetworkD2Pass | D2Pass 维护公告，整站 403 | 等站点恢复 |
| Gcolle | 252 | SiteGcolle | 改版为 Vite SPA，旧 /product_info.php 全部 302 到 /adult/unavailable/content | 需先逆向 SPA 接口，或站点恢复旧页面 |
| ModelMediaAsia | 235 | SiteModelMediaAsia | 官方 API 域名失效（CF 1016/522），上游 2026-04 已删除该 provider | 域名恢复或提供镜像地址 |
| JAVLibrary | 38 | NetworkJAVLibrary | 本机访问被 403（Cloudflare/地区） | 换出口后应可用 |
| R18 | 17 | NetworkR18 | 本机访问被 406/403 | 换出口后应可用 |

## 二、历史批量探测用例（pornrips 批次，lenient 策略）

这批是早期用 pornrips 的 RSS 标题批量建立的，建立时就是"多级回退搜索词、0 结果不失败"的宽松策略，
**不代表站点坏**。建议逐步收紧：先跑单条确认能命中，再把 allowZero 改成 false。

| 用例 id | handler |
|---|---|
| search-5kporn-70-0 | Network5Kporn |
| search-abbywinters-57-0 | SiteAbbyWinters |
| search-auntjudysxxx-73-0 | NetworkAuntJudys |
| search-bang-1-0 | NetworkBang |
| search-biggulpgirls-67-4 | NetworkRadicalCash |
| search-brandnewamateurs-137-0 | SiteBrandNewAmateurs |
| search-digitalplayground-2-14 | Network1service |
| search-familystrokes-24-31 | NetworkMylf |
| search-familytherapyxxx-52-0 | SiteFamilyTherapyXXX |
| search-femjoy-149-0 | SiteFemjoy |
| search-ftvgirls-88-1 | NetworkFTV |
| search-gangbangcreampie-64-75 | NetworkGammaEntOther |
| search-hegre-37-0 | SiteHegre |
| search-hotmilfsfuck-160-6 | SiteInterracialPass |
| search-hunt4k-120-4 | NetworkVIP4K |
| search-jesseloadsmonsterfacials-164-0 | SiteJesseLoadsMonsterFacials |
| search-milfaf-118-0 | NetworkThickCashOther |
| search-missax-176-0 | SiteMissaX |
| search-momishorny-22-35 | Network1service |
| search-mrluckypov-202-8 | SiteSpizoo |
| search-nubiles-28-1 | NetworkNubiles |
| search-penthousegold-183-0 | SitePenthouseGold |
| search-pornfidelity-97-0 | NetworkKellyMadison |
| search-pornworld-106-16 | NetworkPornWorld |
| search-private-190-0 | SitePrivate |
| search-publicagent-8-3 | Network1service |
| search-purecfnm-108-1 | NetworkPureCFNM |
| search-sexmex-199-0 | SiteSexMex |
| search-spankmonster-71-1 | NetworkAdultEmpireCash |
| search-watch4beauty-221-0 | SiteWatch4Beauty |
| search-whippedass-16-29 | NetworkKink |

## 三、已知 0 结果 / 不崩溃即可

| 用例 id | handler | 说明 |
|---|---|---|
| knownzero-legalporno-26-0 | SiteLegalPorno | 站点本身对该类标题长期 0 结果 |
| search-tonightsgirlfriend-59-0 | SiteTonightsGirlfriend | 站点搜索常 0 结果，只验证不崩溃 |
| update-tonightsgirlfriend-59-0 | SiteTonightsGirlfriend | Update 直抓的 NRE 回归用例 |

## 四、本次移植新增并标记放宽的站点

| 站点 | 组 | 说明 |
|---|---|---|
| ModelMediaAsia | 235 | 见上表（按需求支持其优先级，但域名已失效） |
| Gcolle | 252 | 见上表 |
| HeyDouga | 253 | 见上表 |
| D2Pass 家族 | 254 | 见上表 |
| FC2Hub / FC2PPVDB | 248 / 249 | 见上表 |
| MGS / SOD | 243 / 244 | 见上表 |
| Faleno | 237 | 见上表 |
| Cum4K | 21/66 | 见上表 |
