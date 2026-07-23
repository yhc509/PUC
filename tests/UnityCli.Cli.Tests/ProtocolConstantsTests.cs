using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void ProtocolVersion_BumpedToSix_ForProfileCommands()
    {
        Assert.Equal("6", ProtocolConstants.ProtocolVersion);
    }

    [Fact]
    public void ErrorUnauthorized_IsRegistered()
    {
        Assert.Equal("UNAUTHORIZED", ProtocolConstants.ErrorUnauthorized);
    }
}
