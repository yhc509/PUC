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
    public void IsValidTestRunId_AcceptsGeneratedIdShape(string runId)
    {
        Assert.True(ProtocolHelpers.IsValidTestRunId(runId));
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
    public void IsValidTestRunId_RejectsAnythingElse(string runId)
    {
        Assert.False(ProtocolHelpers.IsValidTestRunId(runId));
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
