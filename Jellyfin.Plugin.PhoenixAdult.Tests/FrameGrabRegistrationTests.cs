#if !__EMBY__
using PhoenixAdult.Web;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Offline tests for the frame grab endpoint ownership: a duplicate install (manual build
/// next to the auto-updated one) loads the assembly twice, and only one copy may register
/// the MVC route or every request fails with AmbiguousMatchException.
/// </summary>
public class FrameGrabRegistrationTests
{
    private const string Name = "PhoenixAdult";

    [Fact]
    public void SingleCopy_OwnsEndpoints()
    {
        var self = Copy("2026.1031.612.0", "/plugins/PhoenixAdult_2026.1031.612.0/dll");

        Assert.True(FrameGrabRegistration.ShouldOwnEndpoints(self, new[] { self }));
    }

    [Fact]
    public void NewerCopyLoaded_NewestOwnsEndpoints()
    {
        var older = Copy("2026.1031.612.0", "/plugins/PhoenixAdult_2026.1031.612.0/dll");
        var newer = Copy("2026.1031.1300.0", "/plugins/PhoenixAdult_2026.1031.1300.0/dll");

        Assert.True(FrameGrabRegistration.ShouldOwnEndpoints(newer, new[] { older, newer }));
        Assert.False(FrameGrabRegistration.ShouldOwnEndpoints(older, new[] { older, newer }));
    }

    [Fact]
    public void SameVersion_ExactlyOneCopyOwnsEndpoints()
    {
        var first = Copy("2026.1031.612.0", "/plugins/A/dll");
        var second = Copy("2026.1031.612.0", "/plugins/B/dll");
        var loaded = new[] { first, second };

        Assert.True(FrameGrabRegistration.ShouldOwnEndpoints(first, loaded)
            ^ FrameGrabRegistration.ShouldOwnEndpoints(second, loaded));
    }

    [Fact]
    public void OtherAssemblies_Ignored()
    {
        var self = Copy("2026.1031.612.0", "/plugins/PhoenixAdult_2026.1031.612.0/dll");
        var other = ("Metatube", new Version(2025, 1102, 2200, 0), "/plugins/MetaTube/dll");

        Assert.True(FrameGrabRegistration.ShouldOwnEndpoints(self, new[] { self, other }));
    }

    private static (string Name, Version Version, string Location) Copy(string version, string location)
        => (Name, new Version(version), location);
}
#endif
