# Provider 回归未覆盖清单

> 数据来源：`data/SiteList.json` × `ProviderRegression/ProviderCases.json`。
> 本清单**留待后续处理**。生成新用例的方法与踩坑经验见 [scraper-lessons.md](./scraper-lessons.md)。

## 现状

| 指标 | 数值 |
|---|---|
| handler 总数（SiteIDList） | 186 |
| 已有用例覆盖 | 61 |
| **未覆盖** | **125** |
| ├ 已从 pornrips 拿到标题（可直接生成用例） | 18 |
| └ 未找到 pornrips 标题 | 107 |

> 注：覆盖数会随用例增长而变化；以 `ProviderCases.json` 为准，不在本文手工维护逐条清单。
> 下方 A/B 两组为 2026-10-18 的初始快照，实际生成用例前请重新运行发现脚本/真实 Search 校验。

## A. 已有 pornrips 标题（下一步可直接生成 search 用例）

来源：pornrips feed 第 1–40 页（3955 条去重标题），按站点名标准化匹配。
**生成用例前建议先跑一次真实 Search**——本轮对同类候选做过探查，命中率约 50%，避免产生"预期必 0"的用例。

| handler | 组 | 示例标题 |
|---|---|---|
| `NetworkBellesa` | 76 | BellesaFilms.26.09.26.Lumi.Ray.Isa.Bella.Blake.Blossom.The.Girl.They.Wanted.Both.XXX.1080p.HEVC.x265.PRT |
| `NetworkBlurredMedia` | 77 | HotGuysFuck.25.04.25.Chris.Stevens.And.Sweet.Sophia.XXX.1080p.HEVC.x265.PRT |
| `NetworkDerangedDollars` | 83 | Assylum.26.05.10.Rebel.Rhyder.Ass.Not.Done.Yet.3.XXX.1080p.HEVC.x265.PRT |
| `NetworkEvolvedFights` | 86 | EvolvedFights.26.10.02.Jazmine.Cruz.Vs.Fuck.Buck.XXX.1080p.HEVC.x265.PRT |
| `NetworkFullPornNetwork` | 90 | BadDaddyPOV.26.09.22.Gracie.Gates.Can.I.Go.For.A.Ride.Stepdaddy.XXX.1080p.HEVC.x265.PRT |
| `NetworkLoveHerFilms` | 98 | LoveHerBoobs.26.05.18.Blondie.Bombshell.Gentle.Touch.XXX.1080p.HEVC.x265.PRT |
| `NetworkMetArt` | 99 | ALSScan.26.09.23.Bonnie.Dolce.Dehanchement.XXX.1080p.HEVC.x265.PRT |
| `NetworkNVG` | 101 | NetVideoGirls.26.09.22.Molly.XXX.1080p.HEVC.x265.PRT |
| `NetworkPervCity` | 103 | DPDiva.26.08.25.Nicole.Nichols.Gorgeous.Blonde.First.DP.And.Double.Vaginal.XXX.1080p.HEVC.x265.PRT |
| `NetworkPornPros` | 20, 21, 66 | BBCPie.26.09.19.Nomi.Carter.XXX.1080p.HEVC.x265.PRT |
| `NetworkPorndoe` | 29, 31, 32, 33, 34, 35, 36 | HerLimit.26.10.05.Hazel.Moore.She.Wants.Moore.Anal.XXX.1080p.HEVC.x265.PRT |
| `NetworkRadicalCashOther` | 109 | PurgatoryX.26.09.25.Ameena.Green.XXX.1080p.HEVC.x265.PRT |
| `SiteAlettaOceanLive` | 124 | AlettaOceanLive.2026.Aletta.Ocean.Analysing.XXX.1080p.HEVC.x265.PRT |
| `SiteAlsAngels` | 126 | ALSAngels.26.09.17.Amber.Slassh.Photoshoot.XXX.1080p.HEVC.x265.PRT |
| `SiteDorcelClub` | 146 | DorcelClub.26.08.17.Katrina.Colt.XXX.1080p.HEVC.x265.PRT |
| `SiteFinishesTheJob` | 150 | ManoJob.26.03.20.Aleksa.Mink.XXX.1080p.HEVC.x265.PRT |
| `SiteGirlsRimming` | 155 | GirlsRimming.26.03.28.Blondie.Bombshell.Blondies.Private.Discipline.XXX.1080p.HEVC.x265.PRT |
| `SiteManyVids` | 50 | ManyVids.2026.Mellanie.Monroe.And.Violet.Myers.Massage.Josh.Threesome.XXX.1080p.HEVC.x265.PRT |
| `SiteMomComesFirst` | 177 | MomComesFirst.26.09.30.Amiee.Cambridge.And.Zarina.Noir.Unsupervised.On.Vacation.XXX.1080p.HEVC.x265.PRT |
| `SiteSheSeducedMe` | 60 | SheSeducedMe.26.09.21.Freya.Von.Doom.And.Hope.Fontaine.XXX.1080p.HEVC.x265.PRT |
| `SiteTeenyTaboo` | 208 | TeenyTaboo.26.09.18.Erica.Canfield.XXX.1080p.HEVC.x265.PRT |
| `SiteWUNF` | 224 | WakeUpNFuck.26.09.24.Helena.Cholene.WUNF.448.XXX.1080p.HEVC.x265.PRT |
| `SiteWoodmanCastingX` | 223 | WoodmanCastingX.19.06.18.Megan.Venturi.XXX.1080p.HEVC.x265.PRT |

## B. 未找到 pornrips 标题（需其他来源）

这些 handler 在 pornrips 全量 feed（3955 条）中无对应标题，可能属于 JAV 类、VR 类、元数据聚合类或小众站点。
建议后续从站点首页最新更新、站内搜索关键词或 TPDB 获取标题。

| # | handler | 组 |
|---|---|---|
| 1 | `ActorFreeones` | 43 |
| 2 | `Network18` | 69 |
| 3 | `NetworkAdultEmpire` | 45 |
| 4 | `NetworkBadoinkVR` | 74 |
| 5 | `NetworkCaramelCash` | 78 |
| 6 | `NetworkCaribbeancom` | 39 |
| 7 | `NetworkCherryPimps` | 79 |
| 8 | `NetworkCzechAV` | 81 |
| 9 | `NetworkCzechVR` | 82 |
| 10 | `NetworkDirtyFlix` | 84 |
| 11 | `NetworkFAKings` | 87 |
| 12 | `NetworkFemdomEmpire` | 18 |
| 13 | `NetworkFuelVirtual` | 89 |
| 14 | `NetworkGASM` | 92 |
| 15 | `NetworkGrooby` | 93 |
| 16 | `NetworkHeyzo` | 40 |
| 17 | `NetworkHighTechVR` | 94 |
| 18 | `NetworkIntersec` | 95 |
| 19 | `NetworkJAV` | 42 |
| 20 | `NetworkJAVLibrary` | 38 |
| 21 | `NetworkJavBus` | 96 |
| 22 | `NetworkMetadataAPI` | 48 |
| 23 | `NetworkPKJMedia` | 104 |
| 24 | `NetworkPerfectGonzo` | 102 |
| 25 | `NetworkPornCZ` | 41, 105 |
| 26 | `NetworkPuffy` | 107 |
| 27 | `NetworkR18` | 17 |
| 28 | `NetworkRomero` | 110 |
| 29 | `NetworkSinX` | 112 |
| 30 | `NetworkTeenCoreClub` | 115 |
| 31 | `NetworkThickCash` | 117 |
| 32 | `NetworkUnzipVR` | 119 |
| 33 | `NetworkWankz` | 122 |
| 34 | `SiteAllureMedia` | 125 |
| 35 | `SiteAmourAngels` | 127 |
| 36 | `SiteAngelaWhite` | 129 |
| 37 | `SiteArchAngel` | 130 |
| 38 | `SiteBAMVisions` | 133 |
| 39 | `SiteBelAmi` | 134 |
| 40 | `SiteBlackPayBack` | 135 |
| 41 | `SiteBoundHoneys` | 136 |
| 42 | `SiteClips4Sale` | 49 |
| 43 | `SiteColette` | 139 |
| 44 | `SiteCumLouder` | 141 |
| 45 | `SiteCumbizz` | 140 |
| 46 | `SiteDarkRoomVR` | 142 |
| 47 | `SiteData18Empire` | 143 |
| 48 | `SiteDesperateAmateurs` | 144 |
| 49 | `SiteDorcelVision` | 147 |
| 50 | `SiteExpliciteArt` | 148 |
| 51 | `SiteFit18` | 56 |
| 52 | `SiteFittingRoom` | 152 |
| 53 | `SiteFuckingAwesome` | 153 |
| 54 | `SiteHeavyOnHotties` | 156 |
| 55 | `SiteHollyRandall` | 157 |
| 56 | `SiteInTheCrack` | 161 |
| 57 | `SiteJAVDatabase` | 163 |
| 58 | `SiteJVRPorn` | 165 |
| 59 | `SiteKarups` | 166 |
| 60 | `SiteKillergram` | 167 |
| 61 | `SiteKin8tengoku` | 168 |
| 62 | `SiteLittleCaprice` | 169 |
| 63 | `SiteLustReality` | 171 |
| 64 | `SiteLustomic` | 170 |
| 65 | `SiteMeanaWolf` | 172 |
| 66 | `SiteMelenaMariaRya` | 173 |
| 67 | `SiteMeloneChallenge` | 174 |
| 68 | `SiteMilfVR` | 175 |
| 69 | `SiteMormonGirlz` | 179 |
| 70 | `SiteMyDirtyHobby` | 180 |
| 71 | `SiteNewSensationsOther` | 181 |
| 72 | `SitePJGirls` | 184 |
| 73 | `SitePOVR` | 189 |
| 74 | `SitePornbox` | 187 |
| 75 | `SitePornstarPlatinum` | 188 |
| 76 | `SitePuba` | 191 |
| 77 | `SitePutalocura` | 192 |
| 78 | `SiteQueenSnake` | 193 |
| 79 | `SiteRealityLovers` | 194 |
| 80 | `SiteReidMyLips` | 195 |
| 81 | `SiteScrewMeToo` | 197 |
| 82 | `SiteScrewbox` | 196 |
| 83 | `SiteSexLikeReal` | 198 |
| 84 | `SiteSicflics` | 200 |
| 85 | `SiteSinsLife` | 201 |
| 86 | `SiteStasyQ` | 203 |
| 87 | `SiteStepSecrets` | 204 |
| 88 | `SiteStraponCum` | 205 |
| 89 | `SiteSunnyLaneLive` | 206 |
| 90 | `SiteSwallowBay` | 207 |
| 91 | `SiteTSPlayground` | 62 |
| 92 | `SiteToughLoveX` | 209 |
| 93 | `SiteTwoTGirls` | 210 |
| 94 | `SiteUltrafilms` | 211 |
| 95 | `SiteVIPissy` | 212 |
| 96 | `SiteVRAllure` | 217 |
| 97 | `SiteVRHush` | 218 |
| 98 | `SiteVRLatina` | 219 |
| 99 | `SiteVirtualReal` | 213 |
| 100 | `SiteVirtualTaboo` | 214 |
| 101 | `SiteVivid` | 215 |
| 102 | `SiteVogoV` | 216 |
| 103 | `SiteWeAreHairy` | 222 |
| 104 | `SiteXConfessions` | 226 |
| 105 | `SiteXSinsVR` | 229 |
| 106 | `SiteXVirtual` | 230 |
| 107 | `SiteXart` | 225 |
| 108 | `SiteXevUnleashed` | 227 |
| 109 | `SiteXillimite` | 228 |

## C. 已修复的实现 bug

测试用例发现的实现缺陷，已修复并通过回归复验：

| 站点 | 现象 | 根因 | 修复 |
|---|---|---|---|
| `SiteManyVids` | Search 抛 `NullReferenceException` | 场景页无 `applicationLD` 时 `metadata` 为 null，直接调用 `metadata.Name.EndsWith` | 增加 null / 空 Name / `Creator` 守卫，返回空结果而非抛异常 |
| `SitePlayboyPlus` | GetImages 返回 0 张可下载图 | `"movies/{image}"` 拼出 `movies//165111/...` 双斜杠 → 404 | 复用 `NetworkGammaEnt.GetBestPictureURL` + `"movies" + path`；Search 侧同样修正 |
| `NetworkDogfart` | 同上（潜在） | 同类 `pictures.Last()` + 双斜杠写法 | 同 PlayboyPlus 统一修正 |
| `SiteAnalVids` | GetImages 返回 0 张可下载图 | `data-poster` 中的 `&amp;` 未解码 → CDN 403 | `WebUtility.HtmlDecode`（与 `SiteLegalPorno` 既有写法一致） |
| `NetworkPornWorld` | 同 AnalVids（潜在） | `data-poster` 未解码 | 同上 |
| `NetworkReptyle` | Update 日期/类型永远取错 | sceneID 为 3 段，却用 `Length > 2` / `> 3` 判断 | 改为 `> 1` / `> 2` 并处理空 type 段 |

## D. 第二轮修复（经 FlareSolverr / TPDB 参考）

| 站点 | 现象 | 根因 | 修复 |
|---|---|---|---|
| `NetworkWowNetwork`（WowGirls） | Search 0 结果 | `wowgirlsblog.com` 整站 Cloudflare 挑战（裸请求 403） | 按 `SiteFemjoy`/`NetworkKink` 既有范式接入 `FlareSolverr.GetHtml`（搜索页 + 翻页）。实测 Challenge solved，全链路通过 |
| `SitePornhub` | Update Name 为空 | 场景页用 **POST** 请求 → 站点返回 404 "Page Not Found" | 改 **GET**（Update 与 GetImages 两处）；标题改 `//h1[@class='title']/span`（对齐 TPDB `networkPornhub`） |
| `SitePornhub` | GetImages 0 张 | `div#player img` 已移除 | 改读 `og:image` / `twitter:image`（对齐 TPDB） |
| `NetworkReptyle`（Ask Your Mother） | Update Name 为空 | ① 未映射到 `familybundle`（落默认 `reptyle_bundle` 跨站乱匹配）；② 新版游览站页面已无 `window.__INITIAL_STATE__` | ① 按 TPDB `networkTeamskeet2025` 补 `"ask your mother" => "familybundle"`；② Update/GetImages 改从 tours-store ES 取详情 |
| `SiteJulesJordan` | 日期为空 | 仅当外部传入 `sceneDate` 才设置，页面 `uploadDate` 从未解析 | 解析场景页内嵌 JSON 的 `uploadDate` 兜底 |
| `SiteJacquieEtMichel` | Update Name 为空 | 上一轮误判：实测场景页 200 且 JSON-LD 正常，此前失败为瞬时网络抖动 | 已恢复严格断言并通过 |

**环境说明**：WowGirls 需要 FlareSolverr：

```bash
docker run -d --name flaresolverr -p 8191:8191 ghcr.io/gythialy/flaresolverr:latest
```
