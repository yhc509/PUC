using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class RegistryModelsTests
{
    [Fact]
    public void InstanceRegistry_ActiveProjectRoot_RoundTripsThroughJson()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/Users/me/projects/foo",
            instances = [],
        };

        string json = ProtocolJson.Serialize(registry);
        var deserialized = ProtocolJson.Deserialize<InstanceRegistry>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("/Users/me/projects/foo", deserialized!.activeProjectRoot);
    }

    [Fact]
    public void InstanceRegistry_ActiveProjectHash_StillDeserializableForLegacyMigration()
    {
        string legacyJson = "{\"activeProjectHash\":\"abcdef012345\",\"instances\":[]}";
        var deserialized = ProtocolJson.Deserialize<InstanceRegistry>(legacyJson);

        Assert.NotNull(deserialized);
        Assert.Equal("abcdef012345", deserialized!.activeProjectHash);
    }
}
