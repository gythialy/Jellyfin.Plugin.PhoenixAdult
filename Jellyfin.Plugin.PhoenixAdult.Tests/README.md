# PhoenixAdult 测试

xUnit 测试项目，分两类：

## 1. 离线数据测试（默认，无需网络）

验证 `data/SiteList.json` 与 Sites 实现类的数据完整性：

- JSON 结构完整（Sites / SiteIDList / Abbrieviations）
- 每个 SiteIDList 组号在 Sites 中有对应条目
- 每个 handler 都有对应实现类（大小写不敏感，与运行时 `Type.GetType(ignoreCase: true)` 一致）
- 组内站点名+URL 不重复、URL 格式合法、组内站号唯一

```bash
# 本地
dotnet test Jellyfin.Plugin.PhoenixAdult.Tests

# docker
docker build -t phoenixadult-tests -f Dockerfile.test .
docker run --rm phoenixadult-tests
```

## 2. 在线冒烟测试（`Category=Online`，需要网络）

对 SiteIDList 中每个 handler 实例化并执行一次真实 Search 请求，
验证站点页面可访问、解析器正常工作。并发 8，单请求 45s 超时。
结果写入 `smoke-report.txt`。

```bash
docker run --rm phoenixadult-tests \
  dotnet vstest Jellyfin.Plugin.PhoenixAdult.Tests.dll \
  --TestCaseFilter:"Category=Online"
```

## 路径解析

测试通过 `PHOENIX_DATA_DIR` / `PHOENIX_REPO_ROOT` 环境变量定位 `data/` 目录
（docker 内已设置），本地开发环境自动按仓库结构推断。
