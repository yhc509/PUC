using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class TestResultEnvelopeTests
{
    [Fact]
    public void NormalizeTestResultEnvelope_KeepsCompletedResultSuccessful()
    {
        ResponseEnvelope response = BuildResultEnvelope("Completed");

        ResponseEnvelope normalized = UnityCli.Cli.CliApp.NormalizeTestResultEnvelope(CommandKind.TestRun, response);

        Assert.Equal(ProtocolConstants.StatusSuccess, normalized.status);
        Assert.Null(normalized.error);
    }

    [Fact]
    public void NormalizeTestResultEnvelope_KeepsRunningResultSuccessful()
    {
        ResponseEnvelope response = BuildResultEnvelope("Running");

        ResponseEnvelope normalized = UnityCli.Cli.CliApp.NormalizeTestResultEnvelope(CommandKind.TestRun, response);

        Assert.Equal(ProtocolConstants.StatusSuccess, normalized.status);
        Assert.Null(normalized.error);
    }

    [Theory]
    [InlineData("TimedOut", ProtocolConstants.ErrorTestTimeout)]
    [InlineData("Cancelled", ProtocolConstants.ErrorTestCancelled)]
    [InlineData("Failed", ProtocolConstants.ErrorTestRunFailed)]
    public void NormalizeTestResultEnvelope_ConvertsNonCompletedResultToFailure(string status, string expectedCode)
    {
        ResponseEnvelope response = BuildResultEnvelope(status);

        ResponseEnvelope normalized = UnityCli.Cli.CliApp.NormalizeTestResultEnvelope(CommandKind.TestRun, response);

        Assert.Equal(ProtocolConstants.StatusError, normalized.status);
        Assert.Equal(expectedCode, normalized.error?.code);
        Assert.NotNull(normalized.error?.details);
        Assert.Equal(status, normalized.error!.details!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public void NormalizeTestResultEnvelope_MapsInterruptedFailedResult()
    {
        ResponseEnvelope response = BuildResultEnvelope(
            "Failed",
            [ProtocolConstants.TestRunInterruptedMessage]);

        ResponseEnvelope normalized = UnityCli.Cli.CliApp.NormalizeTestResultEnvelope(CommandKind.TestRun, response);

        Assert.Equal(ProtocolConstants.StatusError, normalized.status);
        Assert.Equal(ProtocolConstants.ErrorTestInterrupted, normalized.error?.code);
        Assert.Equal(ProtocolConstants.TestRunInterruptedMessage, normalized.error?.message);
    }

    [Fact]
    public void NormalizeTestResultEnvelope_KeepsQaRunSequenceTimedOutSuccessful()
    {
        ResponseEnvelope response = BuildQaRunSequenceEnvelope("TimedOut");

        ResponseEnvelope normalized = UnityCli.Cli.CliApp.NormalizeTestResultEnvelope(CommandKind.QaRunSequence, response);

        Assert.Equal(ProtocolConstants.StatusSuccess, normalized.status);
        Assert.Null(normalized.error);
        Assert.Equal("TimedOut", normalized.data!.Value.GetProperty("status").GetString());
    }

    private static ResponseEnvelope BuildResultEnvelope(string status, string[]? warnings = null)
    {
        var payload = new TestRunResultPayload
        {
            runId = "run-1",
            mode = "edit",
            status = status,
            startedAt = "2026-05-16T00:00:00.0000000Z",
            warnings = warnings ?? Array.Empty<string>(),
        };

        return ResponseEnvelope.Success(
            "req-1",
            "target-1",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            123,
            ProtocolConstants.TransportLive);
    }

    private static ResponseEnvelope BuildQaRunSequenceEnvelope(string status)
    {
        var payload = new
        {
            status,
            completedSteps = 1,
            totalSteps = 2,
            hasFailure = true,
            failedStep = new
            {
                index = 1,
                name = "wait for player",
                timeoutMs = 100,
            },
        };

        return ResponseEnvelope.Success(
            "req-1",
            "target-1",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            123,
            ProtocolConstants.TransportLive);
    }
}
