namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Shared paths: repository root and data/.
/// From bin/<Config>/net10.0/ go up four levels.
/// </summary>
public static class TestPaths
{
    public static string RepoRoot { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    public static string DataDir { get; } = Path.Combine(RepoRoot, "data");
}
