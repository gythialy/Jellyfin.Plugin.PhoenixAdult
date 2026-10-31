# Configuration

Settings live on **Dashboard → Plugins → PhoenixAdult**. Defaults are shown below.

## General

| Setting | Default | Description |
|---|---|---|
| Disable Caching | `false` | Turn off HTTP response caching. Useful while debugging a site, but slower and heavier on the site. |
| Disable SSL Certificate Check | `false` | Accept invalid TLS certificates. Only for broken or MITM'ing setups. |
| Use FilePath for matching | `false` | Search using the full file path (folders included) instead of the bare file name. |
| Default SiteName | *(empty)* | Site name prepended to every search when file names do not contain one. |
| Disable Auto Identify | `false` | Do not auto-match items that have no PhoenixAdult ID yet. |

## Network

| Setting | Default | Description |
|---|---|---|
| FlareSolverr URL | `http://localhost:8191/` | Address of a FlareSolverr instance. Empty disables it; a JSON-capable build is required for protected APIs. |
| Proxy Enable / Host / Port / Login / Password | `false` / — | Route provider traffic through an HTTP or SOCKS5 proxy, optionally with credentials. |

## Images

| Setting | Default | Description |
|---|---|---|
| Image Referer rules | `phncdn.com=https://www.pornhub.com/`<br>`javbus.com=https://www.javbus.com/` | One `host=referer` per line. Defaults are pre-filled on upgrade and can be edited; an empty referer (`host=`) disables that host. |
| Disable Image Validation | `false` | Skip the `HEAD` reachability check for images. |
| Disable Image Size | `false` | Skip downloading an image to verify its dimensions (the URL must still be reachable). |
| Use a work cover when an actor has no portrait | `true` | When every portrait source is empty, use the cover of one of the actor's JavDB titles instead (labelled `JavDB (cover)`). |

## JavDB

| Setting | Default | Description |
|---|---|---|
| JavDB Cookie | *(empty)* | Optional `javdb.com` session cookie (paste the whole `Cookie:` request header). Only actor pages and uncensored titles need it; censored titles, covers and samples work without one. See [JavDB](./javdb.md). |

## Search

| Setting | Default | Description |
|---|---|---|
| Multi-source search for ID queries | `true` | When a file name contains a JAV / Madou / FC2 number and no site name, query several matching sites in parallel and merge the results instead of binding to one site. |
| Multi-source per-site timeout | `30` | Per-site timeout in seconds (5–120) for the fan-out above. |
| Multi-source routing | `data/IdRoutes.json` | Which sites a number is searched on is data, not code. Routes match by `IdPrefix`, a `Labels` list (301 MadouQu labels harvested by `scripts/harvest-madou-labels.sh`) or `LabelRegex`; the first matching route wins and the fallback route stays last. Edit `data/IdRoutes.json`, or add `data/IdRoutes.local.json` (never overwritten by Update Database). |
| Use MetadataAPI for not supported sites | `true` | Fall back to [ThePornDB](https://theporndb.net) for sites without a native provider. |
| MetadataAPI Token | *(empty)* | ThePornDB API token used by the fallback above. |

## Metadata

| Setting | Default | Description |
|---|---|---|
| Actor Name Source | `Local Database` | Prefer names from the bundled actor database, or keep names exactly as the site provides them. |
| JAV Actor Naming Style | `Western Style` | Order JAV performer names Western-style or Japanese-style. |
| Genre Sorting Style | `Alphabetical` | Sort genres alphabetically, or move position-based genres (e.g. `Position: ...`) to the end. |
| Disable Actors Saving to Metadata | `false` | Do not write performer metadata. |
| Tag Actors managed by PhoenixAdult | `true` | Tag performers added by the plugin so cleanup tasks can find them. |
| Actor Tag | `XXX` | Tag value used for the option above. |
| Disable Genres Saving to Metadata | `false` | Do not write genre metadata. |

## Scheduled tasks

Under **Dashboard → Scheduled Tasks → PhoenixAdult**:

| Task | Purpose |
|---|---|
| Update Database | Sync `SiteList.json` / `Actors.json` / `Genres.json` from this repository. |
| Add Collection | Create a collection for every studio that has scenes scraped by the plugin. |
| Cleanup Actors | Remove performers previously managed by the plugin that no longer exist. |
| Cleanup Genres | Remove genres previously managed by the plugin that no longer exist. |
| Cleanup Service Data | Drop expired stored tokens and cached service data. |
