using UnityCli.Protocol;
using Xunit;

namespace UnityCli.Cli.Tests;

public class EditorCommandCatalogTests
{
    [Fact]
    public void EditorStop_IsWireCommand_WithDestructiveForceRule()
    {
        var descriptor = CliCommandCatalog.FindByCommand("editor stop");
        Assert.NotNull(descriptor);
        Assert.Equal(ProtocolConstants.CommandEditorQuit, descriptor!.ProtocolCommand);
        Assert.Equal(ForceRule.OnDestructiveOp, descriptor.ForceRule);
        Assert.True(descriptor.CanUseLive);
        Assert.False(descriptor.CanUseLocal);
    }

    [Fact]
    public void EditorLaunch_IsLocalOnly()
    {
        var descriptor = CliCommandCatalog.FindByCommand("editor launch");
        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ProtocolCommand);
        Assert.True(descriptor.CanUseLocal);
        Assert.False(descriptor.CanUseLive);
    }

    [Fact]
    public void EditorQuit_IsInSupportedProtocolCommands()
    {
        Assert.Contains(ProtocolConstants.CommandEditorQuit, CliCommandCatalog.GetSupportedProtocolCommands());
    }

    [Theory]
    [InlineData(ProtocolConstants.CommandScreenshot, true)]
    [InlineData(ProtocolConstants.CommandRecordStart, true)]
    [InlineData(ProtocolConstants.CommandQaUiDump, true)]
    [InlineData(ProtocolConstants.CommandQaWorldDump, true)]
    [InlineData(ProtocolConstants.CommandQaClick, true)]
    [InlineData(ProtocolConstants.CommandQaTap, true)]
    [InlineData(ProtocolConstants.CommandQaSwipe, true)]
    [InlineData(ProtocolConstants.CommandQaWaitUntil, false)]
    [InlineData(ProtocolConstants.CommandQaRunSequence, false)]
    [InlineData(ProtocolConstants.CommandRecordStop, false)]
    [InlineData(ProtocolConstants.CommandStatus, false)]
    [InlineData(ProtocolConstants.CommandEditorQuit, false)]
    public void RequiresGraphics_MatchesRenderingSurface(string protocolCommand, bool expected)
    {
        Assert.Equal(expected, CliCommandCatalog.RequiresGraphics(protocolCommand));
    }
}
