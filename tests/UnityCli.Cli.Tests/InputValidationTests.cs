using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class InputValidationTests
{
    private const string ValidRunId = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(ValidRunId)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public void IsValid32HexId_AcceptsGeneratedIdShape(string runId)
    {
        Assert.True(ProtocolHelpers.IsValid32HexId(runId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../foo")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    [InlineData("abc.json")]
    [InlineData("..")]
    [InlineData(" 0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void IsValid32HexId_RejectsAnythingElse(string runId)
    {
        Assert.False(ProtocolHelpers.IsValid32HexId(runId));
    }

    [Fact]
    public void ParseTestResults_AcceptsGeneratedRunId()
    {
        ParsedCommand parsed = CliArgumentParser.Parse(new[] { "test", "results", "--run-id", ValidRunId });

        Assert.Equal(ValidRunId, parsed.TestRunId);
    }

    [Theory]
    [InlineData("../foo")]
    [InlineData("foo/bar")]
    [InlineData("abc.json")]
    public void ParseTestResults_RejectsTraversalRunId(string runId)
    {
        Assert.Throws<CliUsageException>(
            () => CliArgumentParser.Parse(new[] { "test", "results", "--run-id", runId }));
    }

    [Fact]
    public void ParseRecordStatus_AcceptsGeneratedRecordingId()
    {
        ParsedCommand parsed = CliArgumentParser.Parse(
            new[] { "record", "status", "--recording-id", ValidRunId });

        Assert.Equal(ValidRunId, parsed.RecordRunId);
    }

    [Theory]
    [InlineData("../foo")]
    [InlineData("foo/bar")]
    [InlineData("abc.json")]
    public void ParseRecordStatus_RejectsTraversalRecordingId(string recordingId)
    {
        Assert.Throws<CliUsageException>(
            () => CliArgumentParser.Parse(new[] { "record", "status", "--recording-id", recordingId }));
    }

    [Theory]
    [InlineData("scene")]
    [InlineData("prefab")]
    public void PatchSpec_NonObjectRootDoesNotThrowFromForceGating(string command)
    {
        // Force gating must answer "is this destructive?" without blowing up on a
        // spec it cannot read; the editor is what rejects the malformed spec.
        ParsedCommand parsed = CliArgumentParser.Parse(
            new[] { command, "patch", "--path", "Assets/X." + (command == "scene" ? "unity" : "prefab"), "--spec-json", "[]" });

        Assert.False(CliArgumentParser.ForceRequiredByCatalog(parsed));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"nope\"")]
    [InlineData("7")]
    [InlineData("{\"steps\":[1]}")]
    [InlineData("{\"steps\":[{\"wait\":[3]}]}")]
    public void QaSequenceSpec_RejectsNonObjectShapes(string specJson)
    {
        Assert.Throws<CliUsageException>(
            () => CliArgumentParser.Parse(new[] { "qa", "run-sequence", "--spec-json", specJson }));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"status\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    public void RawEnvelope_RejectsNonObjectRoot(string rawJson)
    {
        Assert.Throws<CliUsageException>(() => BuildRawEnvelope(rawJson));
    }

    [Theory]
    [InlineData("{\"command\":123}")]
    [InlineData("{\"command\":null}")]
    [InlineData("{\"command\":[]}")]
    [InlineData("{\"command\":{}}")]
    [InlineData("{\"command\":\"\"}")]
    [InlineData("{\"command\":\"   \"}")]
    public void RawEnvelope_RejectsNonStringOrEmptyCommand(string rawJson)
    {
        Assert.Throws<CliUsageException>(() => BuildRawEnvelope(rawJson));
    }

    [Fact]
    public void RawEnvelope_RejectsMissingCommand()
    {
        Assert.Throws<CliUsageException>(() => BuildRawEnvelope("{\"arguments\":{}}"));
    }

    [Theory]
    [InlineData("{\"command\":\"status\",\"arguments\":[]}")]
    [InlineData("{\"command\":\"status\",\"arguments\":\"nope\"}")]
    [InlineData("{\"command\":\"status\",\"arguments\":7}")]
    public void RawEnvelope_RejectsNonObjectArguments(string rawJson)
    {
        Assert.Throws<CliUsageException>(() => BuildRawEnvelope(rawJson));
    }

    [Fact]
    public void RawEnvelope_RejectsMalformedJson()
    {
        Assert.Throws<CliUsageException>(() => BuildRawEnvelope("{\"command\":"));
    }

    [Theory]
    [InlineData("{\"command\":\"status\"}")]
    [InlineData("{\"command\":\"status\",\"arguments\":{}}")]
    [InlineData("{\"command\":\"status\",\"arguments\":null}")]
    public void RawEnvelope_AcceptsWellFormedPayload(string rawJson)
    {
        CommandEnvelope envelope = BuildRawEnvelope(rawJson);

        Assert.Equal("status", envelope.command);
    }

    private static CommandEnvelope BuildRawEnvelope(string rawJson)
    {
        return new ParsedCommand(CommandKind.Raw) { RawJson = rawJson }.ToEnvelope();
    }
}
