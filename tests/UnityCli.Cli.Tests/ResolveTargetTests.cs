using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ResolveTargetTests
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
    public void NoProjectRoot_PinnedLive_ReturnsPinned()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/a",
            activeProjectRootPinned = true,
            instances = [Record("/a", "idle"), Record("/b", "idle")],
        };

        var target = UnityCli.Cli.CliApp.ResolveTarget(registry, null);

        Assert.Equal("/a", target!.projectRoot);
    }

    [Fact]
    public void NoProjectRoot_PinnedOffline_FallsThroughToLiveCount()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/a",
            activeProjectRootPinned = true,
            instances = [Record("/a", "offline"), Record("/b", "idle")],
        };

        var target = UnityCli.Cli.CliApp.ResolveTarget(registry, null);

        Assert.Equal("/b", target!.projectRoot);
    }

    [Fact]
    public void NoProjectRoot_PinnedOffline_TwoLive_Throws()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/a",
            activeProjectRootPinned = true,
            instances = [Record("/a", "offline"), Record("/b", "idle"), Record("/c", "idle")],
        };

        Assert.Throws<CliUsageException>(() => UnityCli.Cli.CliApp.ResolveTarget(registry, null));
    }

    [Fact]
    public void NoProjectRoot_NoPin_SingleLive_ReturnsIt()
    {
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "idle"), Record("/b", "offline")],
        };

        var target = UnityCli.Cli.CliApp.ResolveTarget(registry, null);

        Assert.Equal("/a", target!.projectRoot);
    }

    [Fact]
    public void NoProjectRoot_NoPin_TwoLive_Throws()
    {
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "idle"), Record("/b", "idle")],
        };

        var ex = Assert.Throws<CliUsageException>(() => UnityCli.Cli.CliApp.ResolveTarget(registry, null));
        Assert.Contains("/a", ex.Message);
        Assert.Contains("/b", ex.Message);
    }

    [Fact]
    public void NoProjectRoot_NoLive_ReturnsNull()
    {
        var registry = new InstanceRegistry
        {
            instances = [Record("/a", "offline")],
        };

        Assert.Null(UnityCli.Cli.CliApp.ResolveTarget(registry, null));
    }

    [Fact]
    public void NoProjectRoot_UnpinnedActiveProjectRoot_DoesNotShortCircuit_TwoLive_Throws()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/a",
            activeProjectRootPinned = false,
            instances = [Record("/a", "idle"), Record("/b", "idle")],
        };

        Assert.Throws<CliUsageException>(() => UnityCli.Cli.CliApp.ResolveTarget(registry, null));
    }
}
