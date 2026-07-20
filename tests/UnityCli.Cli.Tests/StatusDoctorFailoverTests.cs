using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

// Registry-store-level tests for the #72 "diagnostic failover" narrowing: status/doctor try other
// registry candidates when the resolved (pinned/active) target fails to connect. Mutating command
// dispatch (ExecuteUnityCommandAsync) must keep failing loudly instead of retargeting.
public sealed class StatusDoctorFailoverTests
{
    [Fact]
    public async Task RunStatusAsync_PinnedTargetUnreachable_FallsBackToHealthyLiveInstance()
    {
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string rootB = CreateUnityProject(temp.Path, "ProjectB");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string canonicalB = ProtocolConstants.GetCanonicalPath(rootB);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        string hashB = ProtocolConstants.ComputeProjectHash(canonicalB);
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: canonicalA, rootA, rootB);
        var seenTargets = new List<string>();

        var result = await UnityCli.Cli.CliApp.RunStatusAsync(
            registryStore,
            projectRoot: null,
            (target, _, _, _) =>
            {
                seenTargets.Add(target.projectRoot);
                if (string.Equals(target.projectRoot, canonicalA, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("simulated connection refused");
                }

                return Task.FromResult(Success(new { ok = true }, hashB));
            });

        Assert.Equal([canonicalA, canonicalB], seenTargets);
        Assert.Equal(ProtocolConstants.StatusSuccess, result.status);
        Assert.True(result.data.HasValue);
        JsonElement data = result.data.Value;
        Assert.True(data.GetProperty("ok").GetBoolean());
        JsonElement failedOverFrom = data.GetProperty("failedOverFrom");
        Assert.Equal(hashA, failedOverFrom.GetProperty("projectHash").GetString());
        Assert.Equal(canonicalA, failedOverFrom.GetProperty("projectRoot").GetString());
    }

    [Fact]
    public async Task RunStatusAsync_PinnedTargetUnreachable_NoFallback_StillReportsUnreachable()
    {
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: null, rootA);
        int calls = 0;

        var result = await UnityCli.Cli.CliApp.RunStatusAsync(
            registryStore,
            projectRoot: null,
            (_, _, _, _) =>
            {
                calls++;
                throw new IOException("simulated connection refused");
            });

        Assert.Equal(1, calls);
        Assert.Equal(ProtocolConstants.StatusSuccess, result.status);
        Assert.Equal(hashA, result.target);
        Assert.True(result.data.HasValue);
        Assert.False(result.data!.Value.GetProperty("liveReachable").GetBoolean());
        Assert.False(result.data!.Value.TryGetProperty("failedOverFrom", out _));
    }

    [Fact]
    public async Task RunDoctorAsync_PinnedTargetUnreachable_FallsBackToHealthyLiveInstance()
    {
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string rootB = CreateUnityProject(temp.Path, "ProjectB");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string canonicalB = ProtocolConstants.GetCanonicalPath(rootB);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        string hashB = ProtocolConstants.ComputeProjectHash(canonicalB);
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: canonicalA, rootA, rootB);
        var seenTargets = new List<string>();

        var result = await UnityCli.Cli.CliApp.RunDoctorAsync(
            registryStore,
            new UnityProjectLocator(),
            new ParsedCommand(CommandKind.Doctor),
            projectRoot: null,
            (target, _, _, _) =>
            {
                seenTargets.Add(target.projectRoot);
                if (string.Equals(target.projectRoot, canonicalA, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("simulated connection refused");
                }

                return Task.FromResult(Success(new { }, hashB));
            });

        Assert.Equal([canonicalA, canonicalB], seenTargets);
        Assert.True(result.data.HasValue);
        JsonElement data = result.data.Value;
        Assert.True(data.GetProperty("liveReachable").GetBoolean());
        Assert.Equal(hashB, data.GetProperty("targetProjectHash").GetString());
        JsonElement failedOverFrom = data.GetProperty("failedOverFrom");
        Assert.Equal(hashA, failedOverFrom.GetProperty("projectHash").GetString());
        Assert.Equal(canonicalA, failedOverFrom.GetProperty("projectRoot").GetString());
    }

    [Fact]
    public async Task RunDoctorAsync_PinnedTargetUnreachable_NoFallback_StillReportsUnreachable()
    {
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: null, rootA);
        int calls = 0;

        var result = await UnityCli.Cli.CliApp.RunDoctorAsync(
            registryStore,
            new UnityProjectLocator(),
            new ParsedCommand(CommandKind.Doctor),
            projectRoot: null,
            (_, _, _, _) =>
            {
                calls++;
                throw new IOException("simulated connection refused");
            });

        Assert.Equal(1, calls);
        Assert.True(result.data.HasValue);
        JsonElement data = result.data.Value;
        Assert.False(data.GetProperty("liveReachable").GetBoolean());
        Assert.Equal(hashA, data.GetProperty("targetProjectHash").GetString());
        Assert.False(data.TryGetProperty("failedOverFrom", out _));
    }

    [Fact]
    public async Task RunDoctorAsync_FallbackAnswersWithError_StillReportsFailedOverFrom()
    {
        // Misdiagnosis guard: a fallback that answers with an error (PROTOCOL_MISMATCH on a
        // mixed-version machine) gets its identity and error code reported, so the failover marker
        // must be attached even though liveReachable is false — otherwise the user debugs project A
        // while looking at project B's error.
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string rootB = CreateUnityProject(temp.Path, "ProjectB");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string canonicalB = ProtocolConstants.GetCanonicalPath(rootB);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        string hashB = ProtocolConstants.ComputeProjectHash(canonicalB);
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: canonicalA, rootA, rootB);

        var result = await UnityCli.Cli.CliApp.RunDoctorAsync(
            registryStore,
            new UnityProjectLocator(),
            new ParsedCommand(CommandKind.Doctor),
            projectRoot: null,
            (target, _, _, _) =>
            {
                if (string.Equals(target.projectRoot, canonicalA, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("simulated connection refused");
                }

                return Task.FromResult(ResponseEnvelope.Failure(
                    "req-1",
                    hashB,
                    "PROTOCOL_MISMATCH",
                    "protocol mismatch",
                    retryable: false));
            });

        Assert.True(result.data.HasValue);
        JsonElement data = result.data.Value;
        Assert.False(data.GetProperty("liveReachable").GetBoolean());
        Assert.Equal("PROTOCOL_MISMATCH", data.GetProperty("liveErrorCode").GetString());
        Assert.Equal(hashB, data.GetProperty("targetProjectHash").GetString());
        JsonElement failedOverFrom = data.GetProperty("failedOverFrom");
        Assert.Equal(hashA, failedOverFrom.GetProperty("projectHash").GetString());
        Assert.Equal(canonicalA, failedOverFrom.GetProperty("projectRoot").GetString());
    }

    [Fact]
    public async Task RunStatusAsync_FallbackReturnsNonObjectData_PreservesOriginalPayload()
    {
        // AttachFailoverInfo must never silently discard the payload it is annotating.
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string rootB = CreateUnityProject(temp.Path, "ProjectB");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        string hashB = ProtocolConstants.ComputeProjectHash(ProtocolConstants.GetCanonicalPath(rootB));
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: canonicalA, rootA, rootB);

        var result = await UnityCli.Cli.CliApp.RunStatusAsync(
            registryStore,
            projectRoot: null,
            (target, _, _, _) =>
            {
                if (string.Equals(target.projectRoot, canonicalA, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("simulated connection refused");
                }

                return Task.FromResult(Success(new[] { 1, 2, 3 }, hashB));
            });

        Assert.Equal(ProtocolConstants.StatusSuccess, result.status);
        Assert.True(result.data.HasValue);
        JsonElement data = result.data.Value;
        Assert.Equal([1, 2, 3], data.GetProperty("data").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(hashA, data.GetProperty("failedOverFrom").GetProperty("projectHash").GetString());
    }

    [Fact]
    public async Task ExecuteUnityCommandAsync_PinnedTargetUnreachable_DoesNotFailOverToOtherLiveInstance()
    {
        // Out-of-scope guard: mutating command dispatch must keep failing loudly on an unreachable
        // pinned target rather than silently retargeting a different project's live Editor.
        using var temp = new TempDirectory();
        string rootA = CreateUnityProject(temp.Path, "ProjectA");
        string rootB = CreateUnityProject(temp.Path, "ProjectB");
        string canonicalA = ProtocolConstants.GetCanonicalPath(rootA);
        string hashA = ProtocolConstants.ComputeProjectHash(canonicalA);
        var registryStore = SaveRegistry(temp.Path, pinnedRoot: canonicalA, rootA, rootB);
        var seenTargets = new List<string>();

        var result = await UnityCli.Cli.CliApp.ExecuteUnityCommandAsync(
            new ParsedCommand(CommandKind.Compile),
            registryStore,
            projectRoot: null,
            (target, _, _, _) =>
            {
                seenTargets.Add(target.projectRoot);
                throw new IOException("simulated connection refused");
            });

        Assert.Equal([canonicalA], seenTargets);
        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal("LIVE_UNAVAILABLE", result.error?.code);
        Assert.Equal(hashA, result.target);
    }

    private static ResponseEnvelope Success<T>(T data, string target)
    {
        return ResponseEnvelope.Success(
            "req-1",
            target,
            JsonSerializer.SerializeToElement(data, ProtocolJson.Default),
            durationMs: 1,
            transport: ProtocolConstants.TransportLive);
    }

    private static InstanceRegistryStore SaveRegistry(string tempRoot, string? pinnedRoot, params string[] projectRoots)
    {
        string registryPath = Path.Combine(tempRoot, "instances.json");
        var registryStore = new InstanceRegistryStore(registryPath);
        var instances = projectRoots
            .Select(root =>
            {
                string canonicalRoot = ProtocolConstants.GetCanonicalPath(root);
                string projectHash = ProtocolConstants.ComputeProjectHash(canonicalRoot);
                return new InstanceRecord
                {
                    projectRoot = canonicalRoot,
                    projectName = Path.GetFileName(canonicalRoot),
                    projectHash = projectHash,
                    pipeName = ProtocolConstants.BuildPipeName(projectHash),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                };
            })
            .ToArray();

        registryStore.Save(new InstanceRegistry
        {
            activeProjectRoot = pinnedRoot ?? string.Empty,
            activeProjectRootPinned = pinnedRoot is not null,
            instances = instances,
        });

        return registryStore;
    }

    private static string CreateUnityProject(string root, string name)
    {
        string projectRoot = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Packages"));
        return projectRoot;
    }
}
