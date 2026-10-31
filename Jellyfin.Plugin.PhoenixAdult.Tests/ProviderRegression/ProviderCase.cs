namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// Provider regression case kinds.
/// </summary>
public enum ProviderCaseKind
{
    /// <summary>Handler Search, optionally followed by Update and GetImages.</summary>
    Search,

    /// <summary>Handler Update called with an explicit scene URL.</summary>
    Update,

    /// <summary>MovieProvider Search then GetMetadata.</summary>
    E2e,

    /// <summary>Images fetched through MovieImageProvider and validated like a real download.</summary>
    Images,
}

/// <summary>
/// One provider regression case, loaded from ProviderCases.json.
/// </summary>
public sealed class ProviderCase
{
    /// <summary>Stable case id, unique in the catalog.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Case kind.</summary>
    public ProviderCaseKind Kind { get; init; }

    /// <summary>Handler class name from SiteIDList (search and update cases).</summary>
    public string Handler { get; init; } = string.Empty;

    /// <summary>Site group.</summary>
    public int Group { get; init; }

    /// <summary>Site number inside the group.</summary>
    public int Site { get; init; }

    /// <summary>Site display name for reports.</summary>
    public string SiteName { get; init; } = string.Empty;

    /// <summary>Ordered search terms; fall back to the next one on zero results.</summary>
    public string[] SearchTerms { get; init; } = System.Array.Empty<string>();

    /// <summary>Minimum expected results (0 only requires no crash).</summary>
    public int ExpectMinResults { get; init; } = 1;

    /// <summary>Allow zero results, for known-empty or URL-only sites.</summary>
    public bool AllowZero { get; init; }

    /// <summary>Fragment the first title must contain (optional assertion).</summary>
    public string? ExpectedFirstContains { get; init; }

    /// <summary>Fragment seen in earlier runs (documented only).</summary>
    public string? ExpectedFirstHint { get; init; }

    /// <summary>Run Update and GetImages after a Search hit.</summary>
    public bool Chain { get; init; }

    /// <summary>Update must return a non-empty name.</summary>
    public bool ExpectName { get; init; }

    /// <summary>Minimum actor count from Update.</summary>
    public int ExpectMinActors { get; init; }

    /// <summary>GetImages must return at least one image.</summary>
    public bool ExpectImages { get; init; }

    /// <summary>
    /// search case: minimum share of the first ten results carrying an image after the thumbnail fill (0 disables).
    /// Keeps sites whose list page has no poster covered.
    /// </summary>
    public double MinDialogImageRatio { get; init; }

    /// <summary>
    /// e2e case: minimum share of results carrying an ImageUrl (0 disables).
    /// The dialog renders ImageUrl only, and sample images are HEAD checked.
    /// </summary>
    public double MinSearchImageRatio { get; init; }

    /// <summary>Update must return a premiere date.</summary>
    public bool ExpectPremiereDate { get; init; }

    /// <summary>Scene URL used for a direct Update.</summary>
    public string? SceneUrl { get; init; }

    /// <summary>File name used by e2e cases.</summary>
    public string? FileName { get; init; }

    /// <summary>Any of these fragments must appear in a title (case-insensitive).</summary>
    public string[] ExpectTitleContains { get; init; } = System.Array.Empty<string>();

    /// <summary>Expected premiere date for e2e (yyyy-MM-dd).</summary>
    public string? ExpectDate { get; init; }

    /// <summary>Expected actor names for e2e (case-insensitive).</summary>
    public string[] ExpectActors { get; init; } = System.Array.Empty<string>();

    /// <summary>images case: raw scene id parts passed to GetImages.</summary>
    public string[]? SceneId { get; init; }

    /// <summary>Source RSS title, for traceability.</summary>
    public string? RssTitle { get; init; }

    /// <summary>Case note.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Where the case came from.</summary>
    public string[] Source { get; init; } = System.Array.Empty<string>();

    /// <summary>Site numbers.</summary>
    public int[] SiteNum => new[] { this.Group, this.Site };

    /// <inheritdoc/>
    public override string ToString() => $"{this.Id} [{this.Kind}] {this.SiteName}";
}
