using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void ProtocolVersion_BumpedToFive_ForIpcAuthentication()
    {
        Assert.Equal("5", ProtocolConstants.ProtocolVersion);
    }

    [Fact]
    public void ErrorUnauthorized_IsRegistered()
    {
        Assert.Equal("UNAUTHORIZED", ProtocolConstants.ErrorUnauthorized);
    }
}
