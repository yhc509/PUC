using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class EditorReadyWaitTests
{
    [Fact]
    public async Task PollEditorReadyAsync_WaitsUntilStatusIsNotCompilingOrUpdating()
    {
        var parsed = new ParsedCommand(CommandKind.Compile) { Wait = true };
        var started = Success(new { message = "script compilation 요청 완료" });
        var statusResponses = new Queue<ResponseEnvelope>([
            Success(new StatusPayload { isCompiling = true }),
            Success(new StatusPayload { isUpdating = true }),
            Success(new StatusPayload()),
        ]);
        var polledCommands = new List<string>();

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            started,
            (command, _, _) =>
            {
                polledCommands.Add(command.command);
                return Task.FromResult(statusResponses.Dequeue());
            },
            delayAsync: (_, _) => Task.CompletedTask,
            timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(ProtocolConstants.StatusSuccess, result.status);
        Assert.Equal([ProtocolConstants.CommandStatus, ProtocolConstants.CommandStatus, ProtocolConstants.CommandStatus], polledCommands);
        Assert.True(result.data.HasValue);
        JsonElement data = result.data.Value;
        Assert.Equal("script compilation 요청 완료", data.GetProperty("message").GetString());
        Assert.True(data.GetProperty("ready").GetBoolean());
        Assert.Equal("ready", data.GetProperty("readyMessage").GetString());
        Assert.True(data.GetProperty("waitedMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task PollEditorReadyAsync_TreatsLiveUnavailableAsTransient()
    {
        var parsed = new ParsedCommand(CommandKind.Refresh) { Wait = true };
        var started = Success(new { message = "AssetDatabase.Refresh 완료" });
        var statusResponses = new Queue<ResponseEnvelope>([
            ResponseEnvelope.Failure("poll-1", "target-1", "LIVE_UNAVAILABLE", "Bridge unavailable.", retryable: true),
            Success(new StatusPayload()),
        ]);

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            started,
            (_, _, _) => Task.FromResult(statusResponses.Dequeue()),
            delayAsync: (_, _) => Task.CompletedTask,
            timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(ProtocolConstants.StatusSuccess, result.status);
        Assert.True(result.data.HasValue);
        Assert.True(result.data.Value.GetProperty("ready").GetBoolean());
    }

    [Theory]
    [InlineData(CommandKind.Compile, ProtocolConstants.ErrorCompileWaitTimeout)]
    [InlineData(CommandKind.Refresh, ProtocolConstants.ErrorRefreshWaitTimeout)]
    public async Task PollEditorReadyAsync_TimesOutWithCommandSpecificRetryableError(
        CommandKind kind,
        string expectedCode)
    {
        var parsed = new ParsedCommand(kind) { Wait = true };

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            Success(new { message = "started" }),
            (_, _, _) => throw new InvalidOperationException("Should not poll after timeout."),
            delayAsync: (_, _) => Task.CompletedTask,
            timeout: TimeSpan.Zero);

        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal(expectedCode, result.error?.code);
        Assert.True(result.retryable);
    }

    [Fact]
    public async Task PollEditorReadyAsync_TimesOutAfterPollingLoop()
    {
        var parsed = new ParsedCommand(CommandKind.Compile) { Wait = true };

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            Success(new { message = "started" }),
            (_, _, _) => Task.FromResult(Success(new StatusPayload
            {
                isCompiling = true,
                isUpdating = false,
                projectName = "X",
            })),
            delayAsync: Task.Delay,
            timeout: TimeSpan.FromMilliseconds(120),
            pollInterval: TimeSpan.FromMilliseconds(50));

        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal(ProtocolConstants.ErrorCompileWaitTimeout, result.error?.code);
        Assert.True(result.retryable);
    }

    [Fact]
    public async Task PollEditorReadyAsync_ReturnsLiveUnavailableAfterTransientFailureCap()
    {
        var parsed = new ParsedCommand(CommandKind.Compile) { Wait = true };
        int polls = 0;

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            Success(new { message = "started" }),
            (_, _, _) =>
            {
                polls++;
                return Task.FromResult(ResponseEnvelope.Failure(
                    "poll-1",
                    "target-1",
                    "LIVE_UNAVAILABLE",
                    "Bridge unavailable.",
                    retryable: true));
            },
            delayAsync: (_, _) => Task.CompletedTask,
            timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(6, polls);
        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal("LIVE_UNAVAILABLE", result.error?.code);
        Assert.True(result.retryable);
        Assert.Contains("6 consecutive transport failures", result.error?.details?.GetString());
    }

    [Fact]
    public async Task PollEditorReadyAsync_ReturnsNonRetryablePollFailureImmediately()
    {
        var parsed = new ParsedCommand(CommandKind.Refresh) { Wait = true };
        int polls = 0;

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            Success(new { message = "started" }),
            (_, _, _) =>
            {
                polls++;
                return Task.FromResult(ResponseEnvelope.Failure(
                    "poll-1",
                    "target-1",
                    "STATUS_FAILED",
                    "Status failed.",
                    retryable: false));
            },
            delayAsync: (_, _) => Task.CompletedTask,
            timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(1, polls);
        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal("STATUS_FAILED", result.error?.code);
        Assert.False(result.retryable);
    }

    [Fact]
    public async Task PollEditorReadyAsync_PropagatesCallerCancellation()
    {
        var parsed = new ParsedCommand(CommandKind.Compile) { Wait = true };
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            UnityCli.Cli.CliApp.PollEditorReadyAsync(
                parsed,
                Success(new { message = "started" }),
                (_, _, _) =>
                {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                },
                cts.Token,
                delayAsync: (_, _) => Task.CompletedTask,
                timeout: TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ExecuteUnityCommandAsync_NormalizesOperationCanceledIpcTimeoutToLiveUnavailable()
    {
        using var temp = new TempDirectory();
        string projectRoot = CreateUnityProject(temp.Path, "SampleProject");
        string expectedProjectHash = ProtocolConstants.ComputeProjectHash(projectRoot);
        var registryStore = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var parsed = new ParsedCommand(CommandKind.Compile);

        var result = await UnityCli.Cli.CliApp.ExecuteUnityCommandAsync(
            parsed,
            registryStore,
            projectRoot,
            (_, _, _, _) => throw new OperationCanceledException("simulated IPC timeout"));

        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal("LIVE_UNAVAILABLE", result.error?.code);
        Assert.True(result.retryable);
        Assert.Equal(expectedProjectHash, result.target);
        Assert.Contains("simulated IPC timeout", result.error?.details?.GetString());
    }

    [Fact]
    public async Task PollEditorReadyAsync_FailsFastWhenStatusPayloadIsInvalid()
    {
        var parsed = new ParsedCommand(CommandKind.Refresh) { Wait = true };
        int polls = 0;

        var result = await UnityCli.Cli.CliApp.PollEditorReadyAsync(
            parsed,
            Success(new { message = "started" }),
            (_, _, _) =>
            {
                polls++;
                return Task.FromResult(Success("not-json"));
            },
            delayAsync: (_, _) => Task.CompletedTask,
            timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(1, polls);
        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal("LIVE_UNAVAILABLE", result.error?.code);
        Assert.True(result.retryable);
        Assert.Contains("valid status payload", result.error?.details?.GetString());
    }

    private static ResponseEnvelope Success<T>(T data)
    {
        return ResponseEnvelope.Success(
            "req-1",
            "target-1",
            JsonSerializer.SerializeToElement(data, ProtocolJson.Default),
            durationMs: 1,
            transport: ProtocolConstants.TransportLive);
    }

    private static string CreateUnityProject(string root, string name)
    {
        string projectRoot = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Packages"));
        return projectRoot;
    }
}
