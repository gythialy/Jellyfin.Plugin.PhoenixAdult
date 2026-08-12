namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 统一路径：定位仓库根目录与 data/ 目录。
/// 测试运行目录: &lt;repo&gt;/Jellyfin.Plugin.PhoenixAdult.Tests/bin/&lt;Config&gt;/net10.0/
/// 上溯 4 层到仓库根。
/// </summary>
public static class TestPaths
{
    public static string RepoRoot { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    public static string DataDir { get; } = Path.Combine(RepoRoot, "data");
}
