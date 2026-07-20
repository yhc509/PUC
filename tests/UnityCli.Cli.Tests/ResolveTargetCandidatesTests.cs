using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

// Pure in-memory candidate-ordering tests, mirroring ResolveTargetTests.cs's style (no
// InstanceRegistryStore/Sanitize involved, so fabricated paths like "/a" work directly).
public sealed class ResolveTargetCandidatesTests
{
    private static InstanceRecord Record(string root, string state)
        => new()
        {
            projectRoot = root,
            projectName = Path.GetFileName(root),
            projectHash = "hash" + root.GetHashCode(),
            pipeName = "pipe" + root.GetHashCode(),
            state = state,
            lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
        };

    [Fact]
    public void NoProjectRoot_PinnedLive_OtherLiveExists_ReturnsPinnedFirstThenFallback()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/a",
            activeProjectRootPinned = true,
            instances = [Record("/a", "idle"), Record("/b", "idle")],
        };

        var candidates = UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, null);

        Assert.Equal(2, candidates.Length);
        Assert.Equal("/a", candidates[0].projectRoot);
        Assert.Equal("/b", candidates[1].projectRoot);
    }

    [Fact]
    public void NoProjectRoot_PinnedLive_NoOtherLive_ReturnsPinnedOnly()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/a",
            activeProjectRootPinned = true,
            instances = [Record("/a", "idle"), Record("/b", "offline")],
        };

        var candidates = UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, null);

        Assert.Single(candidates);
        Assert.Equal("/a", candidates[0].projectRoot);
    }

    [Fact]
    public void NoProjectRoot_NoPin_SingleLive_ReturnsSingleCandidate()
    {
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "idle"), Record("/b", "offline")],
        };

        var candidates = UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, null);

        Assert.Single(candidates);
        Assert.Equal("/a", candidates[0].projectRoot);
    }

    [Fact]
    public void NoProjectRoot_NoPin_TwoLive_PropagatesAmbiguousException()
    {
        // Same ambiguous-target semantics as ResolveTarget itself: this method must not paper over
        // an unpinned multi-instance registry by silently picking one.
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "idle"), Record("/b", "idle")],
        };

        Assert.Throws<CliUsageException>(() => UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, null));
    }

    [Fact]
    public void NoProjectRoot_NoLive_ReturnsEmptyArray()
    {
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "offline")],
        };

        var candidates = UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ExplicitProjectRoot_IgnoresOtherLiveInstances_ReturnsSingleCandidate()
    {
        // Explicit (or CWD-detected) targeting must stay exactly as stable as ResolveTarget's own
        // explicit-root branch: no failover candidates are ever offered.
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "idle"), Record("/b", "idle")],
        };

        var candidates = UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, "/a");

        Assert.Single(candidates);
        Assert.Equal("/a", candidates[0].projectRoot);
    }
}
