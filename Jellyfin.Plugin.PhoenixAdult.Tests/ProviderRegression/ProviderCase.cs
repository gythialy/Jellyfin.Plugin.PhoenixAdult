namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// 统一 provider 回归用例类型。
/// </summary>
public enum ProviderCaseKind
{
    /// <summary>直接调用 handler 的 Search（可附带全链路 Update/GetImages）。</summary>
    Search,

    /// <summary>以场景 URL 直接调用 handler 的 Update。</summary>
    Update,

    /// <summary>经 MovieProvider 的 Search→GetMetadata 全链路。</summary>
    E2e,

    /// <summary>经 MovieImageProvider 取图并真实下载校验（与 Jellyfin 自动下载同一路径）。</summary>
    Images,
}

/// <summary>
/// 单条 provider 回归用例，来自 ProviderCases.json。
/// </summary>
public sealed class ProviderCase
{
    /// <summary>稳定用例 ID（目录内唯一）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>用例类型。</summary>
    public ProviderCaseKind Kind { get; init; }

    /// <summary>SiteIDList 中的 handler 实现类名（search/update 用例）。</summary>
    public string Handler { get; init; } = string.Empty;

    /// <summary>SiteIDList 组号。</summary>
    public int Group { get; init; }

    /// <summary>组内站号。</summary>
    public int Site { get; init; }

    /// <summary>站点显示名（报告用）。</summary>
    public string SiteName { get; init; } = string.Empty;

    /// <summary>有序搜索词：首个为主词，返回 0 结果时按顺序回退。</summary>
    public string[] SearchTerms { get; init; } = System.Array.Empty<string>();

    /// <summary>期望的最少结果数（0 表示仅要求不崩溃）。</summary>
    public int ExpectMinResults { get; init; } = 1;

    /// <summary>允许 0 结果（不崩溃即通过，如已知 0 结果或仅 URL 直抓站点）。</summary>
    public bool AllowZero { get; init; }

    /// <summary>首个结果标题应包含的片段（硬断言，可选）。</summary>
    public string? ExpectedFirstContains { get; init; }

    /// <summary>历史测试中观察到的首个结果标题片段（仅记录，不断言）。</summary>
    public string? ExpectedFirstHint { get; init; }

    /// <summary>是否在 Search 命中后继续 Update/GetImages 全链路校验。</summary>
    public bool Chain { get; init; }

    /// <summary>Update 结果必须有非空 Name。</summary>
    public bool ExpectName { get; init; }

    /// <summary>Update 结果最少演员数。</summary>
    public int ExpectMinActors { get; init; }

    /// <summary>GetImages 必须至少返回 1 张图。</summary>
    public bool ExpectImages { get; init; }

    /// <summary>
    /// search 用例：经 MovieProvider 的对话框补图（FillMissingThumbnails）后，前 10 条结果的带图占比下限（0 表示不检查）。
    /// 用于钉住"handler 列表页没有海报、只能靠 GetImages 兜底"的站点。
    /// </summary>
    public double MinDialogImageRatio { get; init; }

    /// <summary>
    /// e2e 用例：经 MovieProvider 搜索后，带 ImageUrl 的结果占比下限（0 表示不检查）。
    /// 识别对话框只渲染 ImageUrl，缺图会退化成纯文字卡；同时样例图会做 HEAD 可达性校验。
    /// </summary>
    public double MinSearchImageRatio { get; init; }

    /// <summary>Update 结果必须有首播日期。</summary>
    public bool ExpectPremiereDate { get; init; }

    /// <summary>Update 直抓使用的场景 URL（相对或绝对）。</summary>
    public string? SceneUrl { get; init; }

    /// <summary>e2e 用例使用的文件名（movie 风格 Site.YY.MM.DD...）。</summary>
    public string? FileName { get; init; }

    /// <summary>e2e/标题断言：结果标题需包含其中任一（不区分大小写）。</summary>
    public string[] ExpectTitleContains { get; init; } = System.Array.Empty<string>();

    /// <summary>e2e 期望首播日期（yyyy-MM-dd）。</summary>
    public string? ExpectDate { get; init; }

    /// <summary>e2e 期望包含的演员名（不区分大小写）。</summary>
    public string[] ExpectActors { get; init; } = System.Array.Empty<string>();

    /// <summary>images 用例：provider.GetImages 返回的原始 sceneID 段（如编码后的场景URL），与 group/site 拼成 ProviderIds。</summary>
    public string[]? SceneId { get; init; }

    /// <summary>来源 pornrips RSS 标题（溯源用，可选）。</summary>
    public string? RssTitle { get; init; }

    /// <summary>用例说明。</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>用例来源（历史测试文件）。</summary>
    public string[] Source { get; init; } = System.Array.Empty<string>();

    /// <summary>siteNum 数组。</summary>
    public int[] SiteNum => new[] { this.Group, this.Site };

    /// <inheritdoc/>
    public override string ToString() => $"{this.Id} [{this.Kind}] {this.SiteName}";
}
