# 本地覆盖文件（*.local.json）

插件的数据文件由 **Update Database** 任务从本仓库的 `data/` 目录下载到插件数据目录。
每次任务运行都会用远端版本覆盖这些文件，所以**不要直接改下载下来的 `SiteList.json` / `Actors.json` / `IdRoutes.json`**，改动会在下一次同步时丢失。

需要长期保留的本地改动请写进对应的 `*.local.json`：这两个文件不参与下载，**Update Database 永远不会覆盖它们**。

| 本地文件 | 覆盖的数据文件 | 合并方式 |
|---|---|---|
| `Actors.local.json` | `Actors.json`（演员别名 / 演员所属片商） | 并集：同名 key 的别名数组追加，本地新增的演员直接加入 |
| `IdRoutes.local.json` | `IdRoutes.json`（番号 → 站点 路由） | 按路由 `Id`：同名路由整条替换，本地新增路由追加到末尾 |

## 文件位置

与 `SiteList.json` 同一个目录——即插件数据目录下的 `data/`（不同平台/版本的具体路径不同，以实际为准）：

    <插件数据目录>/data/Actors.local.json
    <插件数据目录>/data/IdRoutes.local.json

定位方法：在日志里搜 `Creating database directory`（Update Database 首次运行会打印完整路径），或直接按已下载的 `SiteList.json` 的位置找。

文件编码用 UTF-8，JSON 必须严格合法（不能有注释、末尾不能有多余逗号）。

## Actors.local.json

结构与 `Actors.json` 相同，三个可选节点：

```json
{
  "ActorsReplace": {
    "Emiri Momota": ["百多えみり", "Momota Emiri"]
  },
  "ActorsStudioIndexes": {
    "234": ["MadouQu"]
  },
  "ActorsReplaceStudios": {
    "MadouQu": {
      "Emiri Momota": ["百多えみり"]
    }
  }
}
```

- `ActorsReplace`：演员名 → 别名数组（别名会解析成规范名，也就是这个 key）。
- `ActorsStudioIndexes`：片商名 → 站点组号数组（`data/SiteList.json` 里的组号）。
- `ActorsReplaceStudios`：片商分组 → 该组下的演员别名（只在这个片商下生效）。
- 与远端数据是**并集**：远端已有的别名会保留，本地追加的别名会合并进去；同名 key 不会覆盖掉远端别名。

## IdRoutes.local.json

结构与 `IdRoutes.json` 相同，顶层是 `Routes` 数组，每项是一条路由：

```json
{
  "Routes": [
    {
      "Id": "madou",
      "Note": "本地补充的麻豆厂牌",
      "IdPrefix": "",
      "Labels": ["mdsr", "mdx", "xjx", "本地厂牌"],
      "LabelRegex": "^(?:MD|MTVQ)[A-Z0-9]*$",
      "Sites": [[234, 0], [235, 0], [239, 0], [96, 0]]
    },
    {
      "Id": "fc2",
      "IdPrefix": "FC2-PPV-",
      "Sites": [[238, 0], [242, 0]]
    }
  ]
}
```

字段含义（与 `data/IdRoutes.json` 一致）：

| 字段 | 说明 |
|---|---|
| `Id` | 路由名，也是覆盖时的键；与远端同名 = 整条替换 |
| `IdPrefix` | 规范化番号的前缀匹配（如 `FC2-PPV-`），空表示不按前缀匹配 |
| `Labels` | 厂牌白名单（精确匹配，忽略大小写），可由 `scripts/harvest-madou-labels.sh` 采集 |
| `LabelRegex` | 厂牌正则匹配，与 `Labels` 是"或"关系 |
| `Sites` | 命中后并发检索的站点，每项 `[组号, 站点号]`，对应 `data/SiteList.json` |

注意：

- 路由顺序即优先级，**第一个命中的生效**；`IdRoutes.json` 的最后一条是兜底路由（两个匹配字段都为空）。
- 本地新增的路由会被追加到末尾，如果它没有匹配字段就是兜底——放在兜底之后不会生效，需要替换而不是追加时请用相同的 `Id`。
- 站点的组号/站点号必须是 `SiteList.json` 里真实存在的，写错会导致该站点检索不到结果（不会崩溃）。

## 生效方式

保存文件后执行 **Dashboard → Scheduled Tasks → PhoenixAdult → Update Database**（或重启 Jellyfin）即可重新加载：
`Database.LoadAll()` 会在任务结束后调用，日志里会出现 `Actors.local.json merged` / `IdRoutes.local.json merged`。

如果文件有语法错误，日志会打印 `Failed to merge ...`，此时插件继续使用远端数据，不会崩溃。

## 校验

- 离线数据测试：`dotnet test Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj --filter "Category!=Online"`（校验 `SiteList.json` / `IdRoutes.json` 的结构与引用）。
- 本地覆盖文件不在测试范围内，改完请用真实番号在识别对话框里搜一次确认。
