# Development

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

The test project has two layers; see [`Jellyfin.Plugin.PhoenixAdult.Tests/README.md`](../Jellyfin.Plugin.PhoenixAdult.Tests/README.md)
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
(`search`, `update`, `e2e`); `search` cases can continue through `Update` → `GetImages` and verify that images are
downloadable with the same validation path Jellyfin uses. Cases that need an unconfigured FlareSolverr are reported as
skipped, not failed. Sites where "no result" is not a failure are listed in [allowZero sites](./allowzero-sites.md), and
handlers still missing coverage in [regression coverage](./provider-regression-uncovered.md).

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
| `data/` | `SiteList.json` (networks, base URLs, search URLs), `IdRoutes.json` (number to sites routing), `Actors.json`, `Genres.json` |
| `docs/` | User and maintainer docs |
| `scripts/` | `manifest.sh` (manifest entry generation), `provider-regression.sh`, `harvest-madou-labels.sh` |
| `.github/workflows/` | CI (both build configurations plus offline tests), release, nightly, CodeQL |
| `Dockerfile.test` | Containerized test runner |
