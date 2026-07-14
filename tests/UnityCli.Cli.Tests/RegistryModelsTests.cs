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

    [Fact]
    public void InstanceRegistry_ActiveProjectRootPinned_RoundTripsThroughJson()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/Users/me/projects/foo",
            activeProjectRootPinned = true,
            instances = [],
        };

        string json = ProtocolJson.Serialize(registry);
        var deserialized = ProtocolJson.Deserialize<InstanceRegistry>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized!.activeProjectRootPinned);
    }

    [Fact]
    public void InstanceRegistry_ActiveProjectRootPinned_DefaultsFalse()
    {
        string json = "{\"activeProjectRoot\":\"/x\",\"instances\":[]}";
        var deserialized = ProtocolJson.Deserialize<InstanceRegistry>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized!.activeProjectRootPinned);
    }

    [Fact]
    public void InstanceRecord_Token_DoesNotRoundTripThroughJson()
    {
        var registry = new InstanceRegistry
        {
            activeProjectRoot = "/Users/me/projects/foo",
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = "/Users/me/projects/foo",
                    projectName = "foo",
                    projectHash = "abcdef012345",
                    pipeName = "/tmp/unity-cli-abcdef012345.sock",
                    token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    state = "idle",
                    lastSeenUtc = "2026-06-13T00:00:00.0000000Z",
                    capabilities = [],
                },
            ],
        };

        string json = ProtocolJson.Serialize(registry);
        var deserialized = ProtocolJson.Deserialize<InstanceRegistry>(json);

        Assert.DoesNotContain("\"token\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.instances);
        Assert.Equal(string.Empty, deserialized.instances[0].token);
    }

    [Fact]
    public void CommandEnvelope_Token_RoundTripsThroughJson()
    {
        var envelope = new CommandEnvelope
        {
            requestId = "req-1",
            protocolVersion = ProtocolConstants.ProtocolVersion,
            token = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
            command = ProtocolConstants.CommandStatus,
            argumentsJson = "{}",
        };

        string json = ProtocolJson.Serialize(envelope);
        var deserialized = ProtocolJson.Deserialize<CommandEnvelope>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(
            "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
            deserialized!.token);
    }

    [Fact]
    public void InstanceRecord_Token_DefaultsEmptyWhenMissing()
    {
        string json = """
            {
                "instances":[
                    {"projectRoot":"/tmp/project","projectName":"project","projectHash":"abcdef012345","pipeName":"/tmp/unity-cli-abcdef012345.sock"}
                ]
            }
            """;

        var deserialized = ProtocolJson.Deserialize<InstanceRegistry>(json);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.instances);
        Assert.Equal(string.Empty, deserialized.instances[0].token);
    }

    [Fact]
    public void CommandEnvelope_Token_DefaultsEmptyWhenMissing()
    {
        string json = "{\"requestId\":\"req-1\",\"protocolVersion\":\"5\",\"command\":\"status\",\"argumentsJson\":\"{}\"}";

        var deserialized = ProtocolJson.Deserialize<CommandEnvelope>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(string.Empty, deserialized!.token);
    }
}
