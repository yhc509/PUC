using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using Xunit;

namespace UnityCli.Cli.Tests;

public class ProfileParserTests
{
    [Fact]
    public void Parse_ProfileStats_AcceptsOptions()
    {
        var parsed = CliArgumentParser.Parse(["profile", "stats", "--frames", "120", "--preset", "render"]);
        Assert.Equal(CommandKind.ProfileStats, parsed.Kind);
        Assert.Equal(120, parsed.ProfileFrames);
        Assert.Equal("render", parsed.ProfilePreset);
    }

    [Fact]
    public void Parse_ProfileCaptureStart_AcceptsOptions()
    {
        var parsed = CliArgumentParser.Parse(["profile", "capture", "start", "--frames", "300", "--budget-ms", "33.33"]);
        Assert.Equal(CommandKind.ProfileCaptureStart, parsed.Kind);
        Assert.Equal(300, parsed.ProfileFrames);
        Assert.Equal(33.33, parsed.ProfileBudgetMs!.Value, precision: 2);
    }

    [Fact]
    public void Parse_ProfileCaptureStart_FramesAndDurationAreExclusive()
    {
        Assert.Throws<CliUsageException>(() =>
            CliArgumentParser.Parse(["profile", "capture", "start", "--frames", "300", "--duration", "10"]));
    }

    [Fact]
    public void Parse_ProfileCaptureStop_Wait()
    {
        var parsed = CliArgumentParser.Parse(["profile", "capture", "stop", "--wait"]);
        Assert.Equal(CommandKind.ProfileCaptureStop, parsed.Kind);
        Assert.True(parsed.ProfileWait);
    }

    [Fact]
    public void Parse_ProfileStatus_PositionalCaptureId()
    {
        var parsed = CliArgumentParser.Parse(["profile", "status", "0123456789abcdef0123456789abcdef"]);
        Assert.Equal(CommandKind.ProfileStatus, parsed.Kind);
        Assert.Equal("0123456789abcdef0123456789abcdef", parsed.ProfileCaptureId);
    }

    [Fact]
    public void Parse_ProfileAnalyze_MarkerQuery()
    {
        var parsed = CliArgumentParser.Parse(["profile", "analyze", "0123456789abcdef0123456789abcdef", "--marker", "GC.Alloc", "--limit", "10"]);
        Assert.Equal(CommandKind.ProfileAnalyze, parsed.Kind);
        Assert.Equal("0123456789abcdef0123456789abcdef", parsed.ProfileCaptureId);
        Assert.Equal("GC.Alloc", parsed.ProfileAnalyzeMarker);
        Assert.Equal(10, parsed.ProfileLimit);
    }

    [Fact]
    public void Parse_ProfileAnalyze_RequiresCaptureId()
    {
        Assert.Throws<CliUsageException>(() => CliArgumentParser.Parse(["profile", "analyze", "--gc"]));
    }

    [Fact]
    public void Parse_ProfileAnalyze_RequiresExactlyOneQuery()
    {
        Assert.Throws<CliUsageException>(() =>
            CliArgumentParser.Parse(["profile", "analyze", "0123456789abcdef0123456789abcdef"]));
        Assert.Throws<CliUsageException>(() =>
            CliArgumentParser.Parse(["profile", "analyze", "0123456789abcdef0123456789abcdef", "--gc", "--spikes"]));
    }

    [Fact]
    public void Parse_QaRunSequence_ProfileFlag()
    {
        var parsed = CliArgumentParser.Parse([
            "qa", "run-sequence",
            "--spec-json", "{\"steps\":[{\"actions\":[{\"wait\":100}]}]}",
            "--profile",
        ]);
        Assert.Equal(CommandKind.QaRunSequence, parsed.Kind);
        Assert.True(parsed.QaSequenceProfile);
    }

    [Fact]
    public void Parse_Profile_RequiresSubcommand()
    {
        Assert.Throws<CliUsageException>(() => CliArgumentParser.Parse(["profile"]));
    }
}
