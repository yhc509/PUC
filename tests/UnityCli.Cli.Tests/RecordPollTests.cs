using System.Text.Json;
using UnityCli.Cli;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class RecordPollTests
{
    [Fact]
    public async Task PollRecordStatus_ReturnsWhenStatusIsCompleted()
    {
        var target = new InstanceRecord
        {
            projectHash = "target-1",
            projectName = "Sample",
        };
        int call = 0;

        Task<ResponseEnvelope> Fake(
            InstanceRecord _,
            CommandEnvelope command,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            call++;
            var payload = new RecordResultPayload
            {
                recordingId = "abc",
                status = call >= 3 ? "Completed" : "Recording",
                path = "out.mp4",
            };
            return Task.FromResult(Success(payload));
        }

        var startedResponse = Success(new RecordStartedPayload
        {
            recordingId = "abc",
            status = "STARTED",
            durationSeconds = 2,
        });

        var result = await CliApp.PollRecordStatusAsync(
            new ParsedCommand(CommandKind.RecordStart) { RecordDuration = 2, RecordWait = true },
            target,
            Fake,
            startedResponse,
            CancellationToken.None,
            delayAsync: (_, _) => Task.CompletedTask);

        var final = CliApp.DeserializeData<RecordResultPayload>(result);
        Assert.Equal("Completed", final!.status);
        Assert.True(call >= 3);
    }

    [Theory]
    [InlineData("Interrupted", ProtocolConstants.ErrorRecordInterrupted)]
    [InlineData("NotFound", ProtocolConstants.ErrorRecordNotFound)]
    [InlineData("Failed", ProtocolConstants.ErrorRecordFailed)]
    public async Task PollRecordStatus_ConvertsTerminalFailureStatusToFailure(string status, string expectedCode)
    {
        var target = new InstanceRecord
        {
            projectHash = "target-1",
            projectName = "Sample",
        };

        Task<ResponseEnvelope> Fake(
            InstanceRecord _,
            CommandEnvelope command,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var payload = new RecordResultPayload
            {
                recordingId = "abc",
                status = status,
                path = "out.mp4",
            };
            return Task.FromResult(Success(payload));
        }

        var startedResponse = Success(new RecordStartedPayload
        {
            recordingId = "abc",
            status = "STARTED",
            durationSeconds = 2,
        });

        var result = await CliApp.PollRecordStatusAsync(
            new ParsedCommand(CommandKind.RecordStart) { RecordDuration = 2, RecordWait = true },
            target,
            Fake,
            startedResponse,
            CancellationToken.None,
            delayAsync: (_, _) => Task.CompletedTask);

        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal(expectedCode, result.error!.code);
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
