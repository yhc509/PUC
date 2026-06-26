using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using Xunit;

namespace UnityCli.Cli.Tests;

public class RecordParserTests
{
    [Fact]
    public void Parse_RecordStart_AcceptsAllOptions()
    {
        var parsed = CliArgumentParser.Parse([
            "record", "start",
            "--path", "out.mp4",
            "--duration", "10",
            "--fps", "30",
            "--max-width", "640",
            "--wait",
        ]);

        Assert.Equal(CommandKind.RecordStart, parsed.Kind);
        Assert.Equal("out.mp4", parsed.RecordPath);
        Assert.Equal(10, parsed.RecordDuration);
        Assert.Equal(30, parsed.RecordFps);
        Assert.Equal(640, parsed.RecordMaxWidth);
        Assert.True(parsed.RecordWait);
    }

    [Fact]
    public void Parse_RecordStop_HasNoOptions()
    {
        var parsed = CliArgumentParser.Parse(["record", "stop"]);

        Assert.Equal(CommandKind.RecordStop, parsed.Kind);
    }

    [Fact]
    public void Parse_RecordStatus_Parses()
    {
        var parsed = CliArgumentParser.Parse(["record", "status"]);

        Assert.Equal(CommandKind.RecordStatus, parsed.Kind);
    }

    [Fact]
    public void Parse_Record_RequiresSubcommand()
    {
        Assert.Throws<CliUsageException>(() => CliArgumentParser.Parse(["record"]));
    }

    [Fact]
    public void Parse_RecordStart_WaitWithoutDuration_Throws()
    {
        Assert.Throws<CliUsageException>(() =>
            CliArgumentParser.Parse(["record", "start", "--wait"]));
    }
}
