using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Xunit;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using PhoenixAdult.Providers;
using PhoenixAdult.Sites;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>Search outcome, including the term that hit and the fallback order.</summary>
public sealed record SearchOutcome(List<RemoteSearchResult> Results, string UsedTerm, IReadOnlyList<string> Terms);

/// <summary>MovieProvider pipeline outcome.</summary>
public sealed record E2eOutcome(int SearchCount, IReadOnlyList<RemoteSearchResult> SearchResults, MovieInfo Info, MetadataResult<Movie>? Meta);

/// <summary>
/// Provider regression runner: resolve the handler, run Search with term fallback, then Update,
/// and recognise environment failures (FlareSolverr missing).
/// </summary>
internal static class ProviderRegressionRunner
{
    /// <summary>Timeout per search term.</summary>
    internal const int PerTermTimeoutSeconds = 60;

    /// <summary>Resolve the handler for a case; throws when the case itself is broken.</summary>
    internal static IProviderBase ResolveProvider(ProviderCase c)
    {
        TestInitializer.Init();
        return Helper.GetBaseSiteByName(c.Handler)
            ?? throw new InvalidOperationException($"handler 无实现类: {c.Handler} ({c.Id})");
    }

    /// <summary>
    /// Search the terms in order and stop on the first hit; return the last result when all miss.
    /// </summary>
    internal static async Task<SearchOutcome> SearchAsync(IProviderBase provider, ProviderCase c, CancellationToken cancellationToken = default)
    {
        var terms = c.SearchTerms.Length > 0 ? c.SearchTerms : new[] { c.SiteName };
        var results = new List<RemoteSearchResult>();
        var used = terms[0];

        foreach (var term in terms)
        {
            used = term;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(PerTermTimeoutSeconds));
            results = await provider.Search(c.SiteNum, term, null, cts.Token).ConfigureAwait(false);
            if (results.Count > 0)
            {
                break;
            }
        }

        return new SearchOutcome(results, used, terms);
    }

    /// <summary>
    /// Re-run the dialog thumbnail path and return the results with images filled in.
    /// </summary>
    internal static async Task<List<RemoteSearchResult>> FillDialogImagesAsync(int[] siteNum, List<RemoteSearchResult> results, CancellationToken cancellationToken)
    {
        TestInitializer.Init();

        // The thumbnail fill needs the group#site#scene id form that MovieProvider adds; work on a copy,
        // because the chain steps rely on the original parts.
        var copies = results.Select(r => new RemoteSearchResult
        {
            Name = r.Name,
            ImageUrl = r.ImageUrl,
            PremiereDate = r.PremiereDate,
            ProductionYear = r.ProductionYear,
            IndexNumber = r.IndexNumber,
            IndexNumberEnd = r.IndexNumberEnd,
            ParentIndexNumber = r.ParentIndexNumber,
            SearchProviderName = r.SearchProviderName,
            Overview = r.Overview,
            ProviderIds = new Dictionary<string, string>(r.ProviderIds),
        }).ToList();

        var providerName = global::PhoenixAdult.Plugin.Instance.Name;
        foreach (var result in copies)
        {
            if (result.ProviderIds.TryGetValue(providerName, out var externalId) && !string.IsNullOrEmpty(externalId)
                && !externalId.StartsWith($"{siteNum[0]}#{siteNum[1]}#", StringComparison.Ordinal))
            {
                result.ProviderIds[providerName] = $"{siteNum[0]}#{siteNum[1]}#{externalId}";
            }
        }

        await MovieProvider.FillMissingThumbnails(siteNum, copies, cancellationToken).ConfigureAwait(false);
        return copies;
    }

    /// <summary>Update using the scene id from Search.</summary>
    internal static Task<MetadataResult<BaseItem>> UpdateWithSceneIdAsync(IProviderBase provider, ProviderCase c, string[] sceneId, CancellationToken cancellationToken = default)
        => provider.Update(c.SiteNum, sceneId, cancellationToken);

    /// <summary>Update using an explicit scene URL.</summary>
    internal static Task<MetadataResult<BaseItem>> UpdateByUrlAsync(IProviderBase provider, ProviderCase c, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(c.SceneUrl))
        {
            throw new InvalidOperationException($"{c.Id}: update 用例缺少 sceneUrl");
        }

        return provider.Update(c.SiteNum, new[] { Helper.Encode(c.SceneUrl) }, cancellationToken);
    }

    /// <summary>Run GetImages and return the count.</summary>
    internal static async Task<int> CountImagesAsync(IProviderBase provider, ProviderCase c, string[] sceneId, BaseItem item, CancellationToken cancellationToken = default)
        => (await provider.GetImages(c.SiteNum, sceneId, item, cancellationToken).ConfigureAwait(false)).Count();

    /// <summary>
    /// Fetch images through MovieImageProvider and validate them the way Jellyfin does
    /// (HEAD, bytes, size, backdrop width).
    /// </summary>
    internal static async Task<List<RemoteImageInfo>> GetValidatedImagesAsync(ProviderCase c, string[] sceneId, CancellationToken cancellationToken = default)
    {
        // Images cases may run alone; make sure the plugin and database are initialised.
        TestInitializer.Init();

        var providerIds = new[] { c.Group.ToString(CultureInfo.InvariantCulture), c.Site.ToString(CultureInfo.InvariantCulture) }
            .Concat(sceneId);

        var movie = new Movie();
        movie.ProviderIds[global::PhoenixAdult.Plugin.Instance.Name] = string.Join("#", providerIds);
        movie.Name = c.SiteName;

        var images = await new MovieImageProvider().GetImages(movie, cancellationToken).ConfigureAwait(false);

        // Re-run the plugin validation so the result matches what Jellyfin downloads.
        var validated = await ImageHelper.GetImagesSizeAndValidate(images, cancellationToken).ConfigureAwait(false);

        // Invariant: a poster implies a backdrop (a real one or the poster reused).
        var primaries = validated.Where(o => o.Type == ImageType.Primary).ToList();
        if (primaries.Count > 0)
        {
            Assert.True(
                validated.Any(o => o.Type == ImageType.Backdrop),
                $"{c.Id}: 有 Primary 但没有任何 Backdrop（海报兜底未生效）");
        }

        return validated;
    }

    /// <summary>
    /// Extract the handler scene id from a Search result; parts are separated by '#'.
    /// </summary>
    internal static string[] SceneIdFrom(RemoteSearchResult result)
        => result.ProviderIds.Values.First().Split('#');

    /// <summary>Run Search then GetMetadata through MovieProvider.</summary>
    internal static async Task<E2eOutcome> RunE2eAsync(ProviderCase c, CancellationToken cancellationToken = default)
    {
        TestInitializer.Init();
        if (string.IsNullOrEmpty(c.FileName))
        {
            throw new InvalidOperationException($"{c.Id}: e2e 用例缺少 fileName");
        }

        var provider = new MovieProvider();
        var info = new MovieInfo { Name = c.FileName };
        var search = (await provider.GetSearchResults(info, cancellationToken).ConfigureAwait(false)).ToList();
        if (search.Count == 0)
        {
            return new E2eOutcome(0, search, info, null);
        }

        var first = search[0];
        info.ProviderIds = first.ProviderIds;
        info.PremiereDate = first.PremiereDate;
        var meta = await provider.GetMetadata(info, cancellationToken).ConfigureAwait(false);
        return new E2eOutcome(search.Count, search, info, meta);
    }

    /// <summary>
    /// Environment failures (FlareSolverr missing) do not count as regression failures.
    /// </summary>
    internal static bool IsEnvironmentSkip(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            var message = e.Message;
            if (string.IsNullOrEmpty(message))
            {
                continue;
            }

            if (message.Contains("FlareSolverr", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (message.Contains("8191", StringComparison.OrdinalIgnoreCase)
                && (message.Contains("refused", StringComparison.OrdinalIgnoreCase) || message.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>First line of an exception message, truncated.</summary>
    internal static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var line = text.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] : line;
    }
}
