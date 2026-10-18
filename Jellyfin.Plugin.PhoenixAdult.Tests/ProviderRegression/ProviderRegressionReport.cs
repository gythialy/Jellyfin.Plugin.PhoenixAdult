namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// 统一回归报告：各用例以追加方式写入 provider-regression-report.txt（线程安全）。
/// 与 SiteSmokeTests 的 smoke-report.txt 互补，CI/本地均可在测试产物目录取用。
/// </summary>
public static class ProviderRegressionReport
{
    private static readonly object Gate = new();

    private static bool started;

    /// <summary>报告文件路径。</summary>
    public static string ReportPath { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "provider-regression-report.txt");

    /// <summary>追加一行报告。</summary>
    public static void Line(string line)
    {
        lock (Gate)
        {
            if (!started)
            {
                System.IO.File.WriteAllText(ReportPath, $"# Provider 统一回归报告 {DateTime.UtcNow:u}{Environment.NewLine}");
                started = true;
            }

            System.IO.File.AppendAllText(ReportPath, line + Environment.NewLine);
        }
    }
}
