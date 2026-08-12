using UnityCli.Protocol;
using Xunit;

namespace UnityCli.Cli.Tests;

public class ProfileMemoryCatalogTests
{
    [Fact]
    public void ProfileMemory_IsLiveWireCommand_NoForce_NoGraphics()
    {
        var descriptor = CliCommandCatalog.FindByCommand("profile memory");
        Assert.NotNull(descriptor);
        Assert.Equal(ProtocolConstants.CommandProfileMemory, descriptor!.ProtocolCommand);
        Assert.Equal(ForceRule.None, descriptor.ForceRule);
        Assert.True(descriptor.CanUseLive);
        Assert.False(descriptor.CanUseLocal);
        Assert.False(CliCommandCatalog.RequiresGraphics(ProtocolConstants.CommandProfileMemory));
    }

    [Fact]
    public void ProfileMemoryCompare_IsLocalOnly()
    {
        var descriptor = CliCommandCatalog.FindByCommand("profile memory compare");
        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ProtocolCommand);
        Assert.True(descriptor.CanUseLocal);
        Assert.False(descriptor.CanUseLive);
        Assert.Equal(ForceRule.None, descriptor.ForceRule);
    }

    [Fact]
    public void ProfileMemory_IsInSupportedProtocolCommands()
    {
        Assert.Contains(ProtocolConstants.CommandProfileMemory, CliCommandCatalog.GetSupportedProtocolCommands());
    }
}
