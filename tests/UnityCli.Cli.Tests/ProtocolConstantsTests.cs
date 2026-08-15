using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void ProtocolVersion_BumpedToEight_ForAgentFacingScreenshotDefaults()
    {
        Assert.Equal("8", ProtocolConstants.ProtocolVersion);
    }

    [Fact]
    public void ErrorUnauthorized_IsRegistered()
    {
        Assert.Equal("UNAUTHORIZED", ProtocolConstants.ErrorUnauthorized);
    }
}
