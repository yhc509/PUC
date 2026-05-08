using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void ProtocolVersion_BumpedToFour_ForRegistryKeyMigration()
    {
        Assert.Equal("4", ProtocolConstants.ProtocolVersion);
    }
}
