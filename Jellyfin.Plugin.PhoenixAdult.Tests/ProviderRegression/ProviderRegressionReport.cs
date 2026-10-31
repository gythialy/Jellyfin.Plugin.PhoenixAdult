namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// Shared regression report, appended to provider-regression-report.txt (thread safe).
/// </summary>
public static class ProviderRegressionReport
{
    private static readonly object Gate = new();

    private static bool started;

    /// <summary>Report path.</summary>
    public static string ReportPath { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "provider-regression-report.txt");

    /// <summary>Append one line.</summary>
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
