using System;
using System.Collections.Generic;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Providers;
using Xunit;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// The dialog renders Name and ProductionYear, so the search name carries a date prefix;
/// the tests pin its format, idempotence and results without a date.
/// </summary>
public class MovieProviderSearchResultTests
{
    [Fact]
    public void AddDatePrefix_PrependsIsoDate()
    {
        var results = new List<RemoteSearchResult>
        {
            new() { Name = "Some Scene", PremiereDate = new DateTime(2026, 10, 20) },
        };

        MovieProvider.AddDatePrefix(results);

        Assert.Equal("2026-10-20 · Some Scene", results[0].Name);
    }

    [Fact]
    public void AddDatePrefix_IsIdempotent()
    {
        var results = new List<RemoteSearchResult>
        {
            new() { Name = "Some Scene", PremiereDate = new DateTime(2026, 10, 20) },
        };

        MovieProvider.AddDatePrefix(results);
        MovieProvider.AddDatePrefix(results);

        Assert.Equal("2026-10-20 · Some Scene", results[0].Name);
    }

    [Fact]
    public void AddDatePrefix_SkipsResultWithoutDate()
    {
        var results = new List<RemoteSearchResult>
        {
            new() { Name = "No Date" },
            new() { PremiereDate = new DateTime(2024, 1, 2) },
        };

        MovieProvider.AddDatePrefix(results);

        Assert.Equal("No Date", results[0].Name);
        Assert.Null(results[1].Name);
    }

    [Fact]
    public void AddDatePrefix_TrimsNothingButPrefix()
    {
        var results = new List<RemoteSearchResult>
        {
            new() { Name = "  Padded  ", PremiereDate = new DateTime(2023, 5, 6) },
        };

        MovieProvider.AddDatePrefix(results);

        Assert.Equal("2023-05-06 ·   Padded  ", results[0].Name);
    }
}
