using System.Text.Json;
using UnityCli.Cli.Models;
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

    private static ResponseEnvelope Success<T>(T data)
    {
        return ResponseEnvelope.Success(
            "req-1",
            "target-1",
            JsonSerializer.SerializeToElement(data, ProtocolJson.Default),
            durationMs: 1,
            transport: ProtocolConstants.TransportLive);
    }
}
