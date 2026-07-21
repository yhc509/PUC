using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

// Pure in-memory candidate-ordering tests, mirroring ResolveTargetTests.cs's style. The
// no-project-root cases compare registry strings verbatim, so fabricated paths like "/a" work
// directly. The explicit-root case is different: ResolveTarget canonicalizes the requested path,
// and "/a" is not rooted on Windows (it canonicalizes to a drive-qualified form), so that test
// derives its paths from the same canonical function to stay platform-independent.
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
        // explicit-root branch: no failover candidates are ever offered. The requested path is
        // canonicalized before it is matched against the registry, so the registry entry and the
        // expectation both come from GetCanonicalPath — otherwise "/a" would match its own literal
        // on Unix but a drive-qualified form on Windows.
        var rootA = ProtocolConstants.GetCanonicalPath("/a");
        var rootB = ProtocolConstants.GetCanonicalPath("/b");
        var registry = new InstanceRegistry
        {
            instances = [Record(rootA, "idle"), Record(rootB, "idle")],
        };

        var candidates = UnityCli.Cli.CliApp.ResolveTargetCandidates(registry, "/a");

        Assert.Single(candidates);
        Assert.Equal(rootA, candidates[0].projectRoot);
    }
}
