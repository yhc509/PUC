using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void ProtocolVersion_BumpedToSeven_ForHeadlessEditorCommands()
    {
        Assert.Equal("7", ProtocolConstants.ProtocolVersion);
    }

    [Fact]
    public void ErrorUnauthorized_IsRegistered()
    {
        Assert.Equal("UNAUTHORIZED", ProtocolConstants.ErrorUnauthorized);
    }
}
