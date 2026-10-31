<p align="center">
  <img src="./docs/logo.png" width="420" alt="PhoenixAdult" />
</p>

# PhoenixAdult

A metadata provider for **Jellyfin 12+** and **Emby 4.9** that fills an adult video library with metadata pulled directly
from the original site: **242 networks / 2,316 site entries** served by 220 provider handlers.

[![GPL 2.0 License](https://img.shields.io/github/license/gythialy/Jellyfin.Plugin.PhoenixAdult)](./LICENSE)
[![Current Release](https://img.shields.io/github/v/release/gythialy/Jellyfin.Plugin.PhoenixAdult)](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/latest)
[![Nightly build](https://img.shields.io/github/actions/workflow/status/gythialy/Jellyfin.Plugin.PhoenixAdult/nightly.yml?branch=master&label=nightly)](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/tag/nightly)

> A maintained fork of [DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult](https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult).
> All credit for the original implementation goes to the upstream project; this fork tracks it and adds site fixes,
> Jellyfin 12 support, image/Referer handling and a unified provider regression suite.

## Documents

| Document | What it covers |
|---|---|
| [Configuration](./docs/configuration.md) | Every plugin setting, defaults and the scheduled tasks |
| [File naming](./docs/file-naming.md) | Accepted file name layouts and how names are matched |
| [Manual search](./docs/manualsearch.md) | Search syntax for sites without a search endpoint |
| [Supported sites](./docs/sitelist.md) | Generated network/site reference with each site's search type |
| [JavDB](./docs/javdb.md) | The JavDB source, its optional login cookie and the actor cover fallback |
| [Local overrides](./docs/local-overrides.md) | `Actors.local.json` / `IdRoutes.local.json` shape and merge rules |
| [Scraper lessons](./docs/scraper-lessons.md) | Recurring defect patterns when a site breaks |
| [Regression coverage](./docs/provider-regression-uncovered.md) | Handlers still missing a test case |
| [allowZero sites](./docs/allowzero-sites.md) | Sites where "no result" is not a failure |
| [Development](./docs/development.md) | Build, test and repository layout |

## Requirements

| Platform | Version | Notes |
|---|---|---|
| Jellyfin | **12.0+** | Built against `net10.0` / `Jellyfin.Controller` 12.x, `targetAbi` pinned to `12.0.0.0` |
| Emby | **4.9.x** | Built against `net8.0`, distributed as a zip only |
| .NET SDK | **10** | Build time only |

Jellyfin 10.x manifest entries are kept so old installs keep working, but they receive no new builds.

## Install

**Jellyfin repository:** add `https://raw.githubusercontent.com/gythialy/Jellyfin.Plugin.PhoenixAdult/dist/manifest.json`
under **Dashboard → Plugins → Repositories**, then install from the catalog. The manifest lives on the
[`dist` branch](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/tree/dist) and is regenerated per release.

**Manual:** download `Jellyfin.Plugin.PhoenixAdult@v<version>.zip` (or the Emby zip) from the
[latest release](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/latest), extract it into
`<data dir>/plugins/PhoenixAdult/` and restart. See the official
[plugin installation docs](https://jellyfin.org/docs/general/server/plugins/) for the data directory.

**Nightly:** untagged builds from `master` are published as the [`nightly`](https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/tag/nightly)
prerelease for testing only; the repository manifest does not pick it up.

## Features

- **Metadata** from the original site: title, summary, studio, release date, genres, performers, posters and backgrounds.
- **Provider IDs and source links**: each match stores the plugin ID and the scraped scene URL as an external link.
- **Identification previews**: the identify dialog shows posters with full release dates, and multi-source searches label
  the site each result came from.
- **Anti-bot handling**: optional [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) integration, a built-in
  Cloudflare Turnstile solver and an HTTP/SOCKS5 proxy.
- **Image hardening**: per-host `Referer` rules plus an image proxy, so signed CDN URLs are validated and downloaded with
  the same headers.
- **Data-driven database**: `SiteList.json`, `IdRoutes.json`, `Actors.json` and `Genres.json` sync through a scheduled
  task, so new sites and performers do not need a plugin release.

## Configuration

All settings live on **Dashboard → Plugins → PhoenixAdult**; the [configuration doc](./docs/configuration.md) lists every
option with its default. The ones worth knowing first:

| Setting | Default | Why |
|---|---|---|
| FlareSolverr URL | `http://localhost:8191/` | Required for sites behind Cloudflare; empty disables it |
| Image Referer rules | `phncdn.com=…`, `javbus.com=…` | One `host=referer` per line; `host=` disables a built-in rule |
| JavDB Cookie | *(empty)* | Only for JavDB actor pages and uncensored titles |
| Multi-source search for ID queries | `true` | Query several sites for a number instead of one |
| MetadataAPI Token | *(empty)* | ThePornDB fallback for sites without a handler |

## Build and test

```bash
dotnet build Jellyfin.Plugin.PhoenixAdult/PhoenixAdult.csproj -c Release        # Jellyfin
dotnet build Jellyfin.Plugin.PhoenixAdult/PhoenixAdult.csproj -c Release.Emby   # Emby
dotnet test Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj -c Release --filter "Category!=Online"
```

See [development](./docs/development.md) for the release configurations, the online regression suite and the repository
layout.

## Contributing

- Bug reports should include platform, plugin version, scene URL, file name and logs.
- Site fixes must keep `-c Release` and `-c Release.Emby` green, pass the offline tests and add a case to
  `ProviderRegression/ProviderCases.json`.
- `master` must stay green; releases are cut from it.

## License

[GPL-2.0](./LICENSE). The plugin automates access to third-party sites and stores no content itself; you are responsible
for complying with the terms of service and the laws that apply where you live.
