using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// ProviderCases.json 目录根。
/// </summary>
public sealed class ProviderCatalog
{
    /// <summary>目录版本。</summary>
    public int Version { get; init; }

    /// <summary>目录说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>历史来源列表。</summary>
    public List<string> GeneratedFrom { get; init; } = new();

    /// <summary>全部用例。</summary>
    public List<ProviderCase> Cases { get; init; } = new();
}

/// <summary>
/// 加载内嵌的 ProviderCases.json 统一用例目录。
/// </summary>
public static class ProviderCaseCatalog
{
    private const string ResourceSuffix = "ProviderCases.json";

    private static readonly Lazy<ProviderCatalog> Instance = new(Load);

    /// <summary>当前目录。</summary>
    public static ProviderCatalog Current => Instance.Value;

    /// <summary>按类型筛选。</summary>
    public static IEnumerable<ProviderCase> OfKind(ProviderCaseKind kind)
        => Current.Cases.Where(c => c.Kind == kind);

    private static ProviderCatalog Load()
    {
        var assembly = typeof(ProviderCaseCatalog).Assembly;
        var resourceName = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"内嵌资源 {ResourceSuffix} 不存在（检查 csproj 的 EmbeddedResource）");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"无法读取内嵌资源 {resourceName}");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return JsonSerializer.Deserialize<ProviderCatalog>(stream, options)
            ?? throw new InvalidOperationException("ProviderCases.json 反序列化失败");
    }
}
