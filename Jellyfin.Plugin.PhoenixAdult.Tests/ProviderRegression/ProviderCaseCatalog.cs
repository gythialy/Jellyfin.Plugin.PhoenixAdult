using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// Root of ProviderCases.json.
/// </summary>
public sealed class ProviderCatalog
{
    /// <summary>Catalog version.</summary>
    public int Version { get; init; }

    /// <summary>Catalog note.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Sources the cases came from.</summary>
    public List<string> GeneratedFrom { get; init; } = new();

    /// <summary>All cases.</summary>
    public List<ProviderCase> Cases { get; init; } = new();
}

/// <summary>
/// Load the embedded case catalog.
/// </summary>
public static class ProviderCaseCatalog
{
    private const string ResourceSuffix = "ProviderCases.json";

    private static readonly Lazy<ProviderCatalog> Instance = new(Load);

    /// <summary>Current catalog.</summary>
    public static ProviderCatalog Current => Instance.Value;

    /// <summary>Filter by kind.</summary>
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
