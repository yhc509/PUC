using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class LiveTimeoutTests
{
    [Fact]
    public void ResolveLiveTimeoutMs_OutlivesDefaultExecuteDeadline()
    {
        int resolved = CliApp.ResolveLiveTimeoutMs(new ParsedCommand(CommandKind.ExecuteCode));

        Assert.Equal(
            ProtocolConstants.DefaultExecuteTimeoutMs + ProtocolConstants.DefaultLiveTimeoutMs,
            resolved);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(300)]
    [InlineData(600)]
    public void ResolveLiveTimeoutMs_OutlivesRequestedExecuteDeadline(int timeoutSeconds)
    {
        int resolved = CliApp.ResolveLiveTimeoutMs(new ParsedCommand(CommandKind.ExecuteCode)
        {
            ExecuteCodeTimeoutSeconds = timeoutSeconds,
        });

        // Pinned rather than `> deadline`: a short deadline still clears the old
        // 30 s default, so a loose assertion would pass against the bug.
        Assert.Equal((timeoutSeconds * 1000) + ProtocolConstants.DefaultLiveTimeoutMs, resolved);
    }

    [Fact]
    public void ResolveLiveTimeoutMs_KeepsLargerExplicitTimeoutForExecute()
    {
        int explicitTimeoutMs = (ProtocolConstants.MaxExecuteTimeoutMs * 2) + ProtocolConstants.DefaultLiveTimeoutMs;

        int resolved = CliApp.ResolveLiveTimeoutMs(new ParsedCommand(CommandKind.ExecuteCode)
        {
            ExecuteCodeTimeoutSeconds = 1,
            TimeoutMs = explicitTimeoutMs,
        });

        Assert.Equal(explicitTimeoutMs, resolved);
    }

    [Fact]
    public void ResolveLiveTimeoutMs_LeavesOtherCommandsOnTheirRequestedTimeout()
    {
        var parsed = new ParsedCommand(CommandKind.SceneInspect);

        Assert.Equal(parsed.TimeoutMs, CliApp.ResolveLiveTimeoutMs(parsed));
    }

    [Fact]
    public void ResolveLiveTimeoutMs_OutlivesTestRunCancelGrace()
    {
        int timeoutSeconds = 120;

        int resolved = CliApp.ResolveLiveTimeoutMs(new ParsedCommand(CommandKind.TestRun)
        {
            TestTimeoutSeconds = timeoutSeconds,
        });

        Assert.True(resolved > (timeoutSeconds + ProtocolConstants.TestRunCancelGraceSeconds) * 1000);
    }
}
