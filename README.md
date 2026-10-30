<p align="center">
  <img src="./docs/logo.png" width="420" alt="PhoenixAdult" />
</p>

# PhoenixAdult

A metadata provider for **Jellyfin 12+** and **Emby 4.9** that fills your adult video library with metadata pulled
directly from the original site — **242 networks / 2,316 site entries**, served by 220 provider handlers.

[![GPL 2.0 License](https://img.shields.io/github/license/gythialy/Jellyfin.Plugin.PhoenixAdult)](./LICENSE)
[![Current Release](https://img.shields.io/github/v/release/gythialy/Jellyfin.Plugin.PhoenixAdult)](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/latest)
[![Nightly build](https://img.shields.io/github/actions/workflow/status/gythialy/Jellyfin.Plugin.PhoenixAdult/nightly.yml?branch=master&label=nightly)](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/tag/nightly)

> This is a maintained fork of [DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult](https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult).
> All credits for the original implementation go to the upstream project; this fork tracks it and adds site fixes,
> Jellyfin 12 support, image/Referer handling and a unified provider regression suite.

------------

## Requirements

| Platform | Supported version | Notes |
|---|---|---|
| Jellyfin | **12.0+** | Built against `net10.0` / `Jellyfin.Controller` 12.x; releases pin `targetAbi` to `12.0.0.0` |
| Emby | **4.9.x** | Built against `net8.0` / `MediaBrowser.Server.Core` 4.9.1.80; distributed as a zip only, no Emby repository manifest is published |
| Build toolchain | .NET SDK **10** | Only needed to build from source (`mise.toml` pins `dotnet = 10`); not required at runtime |

The plugin is Jellyfin 12-only since `v2026.912.926.0`. Older manifest entries for Jellyfin 10.x are kept in the
repository so existing installs keep working, but they no longer receive new builds or fixes.

## Install

### Repository (Jellyfin only)

1. Open **Dashboard → Plugins → Repositories**.
2. Add `PhoenixAdult` with this URL:

   ```
   https://raw.githubusercontent.com/gythialy/Jellyfin.Plugin.PhoenixAdult/dist/manifest.json
   ```

3. Install **PhoenixAdult** from **Dashboard → Plugins → Catalog**.

The manifest lives on the [`dist` branch](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/tree/dist) and is
regenerated on every release. No Emby repository manifest is published, so use the manual archive below.

### Manual

1. Download the archive for your platform from the [latest release](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/latest):
   - `Jellyfin.Plugin.PhoenixAdult@v<version>.zip` for Jellyfin
   - `Emby.Plugin.PhoenixAdult@v<version>.zip` for Emby
2. Extract it into the server's plugin directory and restart:
   - Jellyfin: `<data dir>/plugins/PhoenixAdult/`
   - Emby: `<data dir>/plugins/PhoenixAdult/`
3. Follow the official [plugin installation docs](https://jellyfin.org/docs/general/server/plugins/) if you are unsure
   where the data directory is.

### Nightly

Untagged builds from `master` are published continuously as the [`nightly`](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/tag/nightly)
prerelease (`100.0.0-nightly`). Use it only for testing — it is not picked up by the repository manifest.

## Features

- **Metadata** for scenes, pulled from the original site:
  - Title, summary / plot, studio, release date
  - Genres / categories / tags
  - Performers (actors), with optional actor tagging
  - Poster(s) and background art, including CDNs that require a `Referer` header
- **Provider IDs and source links**: each matched movie stores the `PhoenixAdult` ID and the scraped scene URL, so the
  movie page links back to the site it came from as an external URL.
- **Manual search** for sites without a search endpoint: `SceneID`, `StudioID`, `ActressID`, direct scene URLs, dates
  and site abbreviations. See [manual searching](./docs/manualsearch.md).
- **Anti-bot handling**: optional [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) integration and a built-in
  Cloudflare Turnstile proof-of-work solver for protected sites.
- **Proxy support**: route provider traffic through an HTTP or SOCKS5 proxy, with optional authentication.
- **Image hardening**: per-host `Referer` rules (overridable from the plugin page) plus size/reachability validation, so
  signed CDN URLs are validated with the same headers used for the final download.
- **Automatic database updates**: a scheduled task syncs `data/SiteList.json`, `Actors.json` and `Genres.json` from this
  repository, so new sites and performers do not require a plugin release.
- **Identification previews**: when a file cannot be matched automatically, the identify dialog shows candidate results
  with posters and full release dates, not just titles.

## File Naming

Recommended structures (the site name may be a full name or a site abbreviation):

- `SiteName` - `YYYY-MM-DD` - `Scene Name` `.[ext]`
- `SiteName` - `Scene Name` `.[ext]`
- `SiteName` - `YYYY-MM-DD` - `Actor(s)` `.[ext]`
- `SiteName` - `Actor(s)` `.[ext]`

Real-world examples:

- `Blacked - 2018-12-11 - The Real Thing.mp4`
- `Blacked - Hot Vacation Adventures.mp4`
- `Blacked - 2018-09-07 - Alecia Fox.mp4`

If you prefer to embed scene IDs in filenames instead of matching manually in Jellyfin:

- `SiteName` - `YYYY-MM-DD` - `SceneID` `.[ext]`
- `SiteName` - `SceneID` `.[ext]`
- `SiteName` - `SceneID` - `Scene Name` `.[ext]`

Examples:

- `EvilAngel - 2016-10-02 - 119883` from `https://www.evilangel.com/en/video/Allie--Lilys-Slobbery-Anal-Threesome/119883`
- `MomsTeachSex - 314082` from `https://momsteachsex.com/tube/watch/314082`
- `Babes - 3075191 - Give In to Desire` from `https://www.babes.com/scene/3075191/1`

Some sites have no search function at all; that is what SceneID, StudioID, ActressID and direct URLs are for.

## Supported Networks

The full, generated list of networks and sites — with each site's supported search type — is in
[the sitelist doc](./docs/sitelist.md).

## Configuration

Plugin settings are on **Dashboard → Plugins → PhoenixAdult**. Defaults are shown below.

| Setting | Default | Description |
|---|---|---|
| Disable Caching | `false` | Turn off HTTP response caching. Useful while debugging a site, but slower and heavier on the site. |
| Disable SSL Certificate Check | `false` | Accept invalid TLS certificates. Only for broken/MITM'ing setups. |
| FlareSolverr URL | `http://localhost:8191/` | Address of a FlareSolverr instance. Empty disables FlareSolverr; a JSON-capable build (e.g. the fork with `contentType: json` support) is required for protected APIs. |
| Image Referer rules | *(empty)* | One `host=referer` per line. Appends to or overrides the built-in rules; an empty referer disables a built-in rule for that host. |
| Proxy Enable / Host / Port / Login / Password | `false` / — | Route provider traffic through an HTTP or SOCKS5 proxy, optionally with credentials. |
| Use FilePath for matching | `false` | Search using the full file path (folders included) instead of the bare file name. |
| Default SiteName | *(empty)* | Site name prepended to every search when filenames do not contain one. |
| Use MetadataAPI for not supported sites | `true` | Fall back to [ThePornDB](https://theporndb.net) for sites without a native provider. |
| MetadataAPI Token | *(empty)* | ThePornDB API token used by the fallback above. |
| Disable Actors Saving to Metadata | `false` | Do not write performer metadata. |
| Tag Actors managed by PhoenixAdult | `true` | Tag performers added by the plugin so cleanup tasks can find them. |
| Actor Tag | `XXX` | Tag value used for the option above. |
| Disable Genres Saving to Metadata | `false` | Do not write genre metadata. |
| Disable Image Validation | `false` | Skip the `HEAD` reachability check for images. |
| Disable Image Size | `false` | Skip downloading an image to verify its dimensions (still requires a reachable URL). |
| Disable Auto Identify | `false` | Do not auto-match items that have no PhoenixAdult ID yet. |
| Multi-source search for ID queries | `true` | When a file name contains a JAV / Madou / FC2 number and no site name, query several matching sites in parallel and merge the results instead of binding to one site. |
| Multi-source per-site timeout | `30` | Per-site timeout in seconds (5–120) for the fan-out above. |
| Multi-source routing | `data/IdRoutes.json` | Which sites a number is searched on is data, not code. Edit `data/IdRoutes.json` (or add `data/IdRoutes.local.json`, which the Update Database task never overwrites) to change routes; the first matching route wins and the fallback route stays last. |
| Actor Name Source | `Local Database` | Prefer names from the bundled actor database, or keep names exactly as the site provides them. |
| JAV Actor Naming Style | `Western Style` | Order JAV performer names Western-style or Japanese-style. |
| Genre Sorting Style | `Alphabetical` | Sort genres alphabetically, or move position-based genres (e.g. `Position: ...`) to the end. |

### Scheduled tasks

Under **Dashboard → Scheduled Tasks → PhoenixAdult**:

| Task | Purpose |
|---|---|
| Update Database | Sync `SiteList.json` / `Actors.json` / `Genres.json` from this repository. |
| Add Collection | Create a collection for every studio that has scenes scraped by the plugin. |
| Cleanup Actors | Remove performers previously managed by the plugin that no longer exist. |
| Cleanup Genres | Remove genres previously managed by the plugin that no longer exist. |
| Cleanup Service Data | Drop expired stored tokens / cached service data. |

## Build from source

```bash
# .NET 10 SDK (mise install if you use mise)
dotnet build Jellyfin.Plugin.PhoenixAdult/PhoenixAdult.csproj -c Release
dotnet build Jellyfin.Plugin.PhoenixAdult/PhoenixAdult.csproj -c Release.Emby
```

| Configuration | Target | Output |
|---|---|---|
| `Debug` / `Release` | Jellyfin (`net10.0`) | `Jellyfin.Plugin.PhoenixAdult@v<version>.zip` |
| `Debug.Emby` / `Release.Emby` | Emby (`net8.0`, `__EMBY__` symbols) | `Emby.Plugin.PhoenixAdult@v<version>.zip` |

Both release configurations repack their dependencies with ILRepack into a single assembly and write the zip to the
repository root. Releases use the workflow-generated version, e.g. `/p:Version=2026.1021.1714.0`.

## Testing

The test project has two layers; see [`Jellyfin.Plugin.PhoenixAdult.Tests/README.md`](./Jellyfin.Plugin.PhoenixAdult.Tests/README.md)
for details.

```bash
# Offline: data integrity + regression catalog consistency (what CI runs)
dotnet test Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj -c Release --filter "Category!=Online"

# Online: unified provider regression (~5-6 minutes, hits real sites)
dotnet test Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj -c Release \
  --filter "Category=Online&FullyQualifiedName~ProviderRegression"
# equivalent helper
scripts/provider-regression.sh
```

Online cases are data-driven from `Jellyfin.Plugin.PhoenixAdult.Tests/ProviderRegression/ProviderCases.json`
(81 cases: `search`, `update`, `e2e`); `search` cases optionally continue through `Update` → `GetImages` and verify
that images are actually downloadable with the same validation path Jellyfin uses. Cases that require an unconfigured
FlareSolverr are reported as skipped, not failed.

Docker runner (isolated, .NET 10 SDK image, offline tests by default):

```bash
docker build -t phoenixadult-tests -f Dockerfile.test .
docker run --rm phoenixadult-tests
# override the entrypoint to run the online regression instead
docker run --rm phoenixadult-tests dotnet test -c Release --no-build \
  --filter "Category=Online&FullyQualifiedName~ProviderRegression"
```

## Repository layout

| Path | Contents |
|---|---|
| `Jellyfin.Plugin.PhoenixAdult/` | Plugin assembly: `Sites/` (one handler per network/site), `Providers/`, `Helpers/`, `Configuration/`, `ScheduledTasks/`, `ExternalIds/` |
| `Jellyfin.Plugin.PhoenixAdult.Tests/` | xUnit tests: offline data checks, provider regression suite |
| `data/` | `SiteList.json` (networks, base URLs, search URLs), `IdRoutes.json` (number -> sites routing), `Actors.json`, `Genres.json` |
| `docs/` | User and maintainer docs (see below) |
| `scripts/` | `manifest.sh` (manifest entry generation), `provider-regression.sh` |
| `.github/workflows/` | CI (both build configurations + offline tests), release, nightly, CodeQL |
| `Dockerfile.test` | Containerized test runner |

## Documentation

- [Manual search syntax](./docs/manualsearch.md) — search types and query composition
- [Supported sites](./docs/sitelist.md) — generated network/site reference
- [Scraper lessons](./docs/scraper-lessons.md) — recurring defect patterns and debugging notes
- [Provider regression coverage](./docs/provider-regression-uncovered.md) — handlers still missing test cases
- [allowZero sites](./docs/allowzero-sites.md) — sites kept in the regression suite as "no result is not a failure" (geo/Cloudflare blocks, maintenance, redesigns) and how to re-verify them

## Contributing

- Bug reports should include platform (Jellyfin or Emby), plugin version, scene URL, file name and logs — see the
  [issue template](./.github/ISSUE_TEMPLATE/bug_report.md).
- Site fixes should keep both build configurations green (`-c Release` and `-c Release.Emby`) and pass the offline
  tests. When you fix a scrape, add or refresh a case in `ProviderRegression/ProviderCases.json` so the site stays
  covered.
- `master` must stay green; releases are cut from it via the release workflow.

## License

[GPL-2.0](./LICENSE). The plugin automates access to third-party sites and stores no content itself; you are
responsible for complying with the terms of service and the laws that apply where you live.
