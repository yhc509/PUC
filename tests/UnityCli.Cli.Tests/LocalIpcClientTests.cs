using System.Text.Json;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class LocalIpcClientTests
{
    [Fact]
    public void EnsureCompatibleResponse_WithMatchingProtocol_ReturnsOriginalResponse()
    {
        var response = ResponseEnvelope.Success(
            "req-1",
            "target-1",
            JsonSerializer.Deserialize<JsonElement>("{\"ok\":true}", ProtocolJson.Default),
            12);

        var result = LocalIpcClient.EnsureCompatibleResponse(response);

        Assert.Same(response, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    public void EnsureCompatibleResponse_WithMissingOrMismatchedProtocol_ReturnsProtocolMismatch(string? protocolVersion)
    {
        var response = ResponseEnvelope.Success(
            "req-1",
            "target-1",
            JsonSerializer.Deserialize<JsonElement>("{\"ok\":true}", ProtocolJson.Default),
            12);
        response.protocolVersion = protocolVersion;

        var result = LocalIpcClient.EnsureCompatibleResponse(response);

        Assert.Equal(ProtocolConstants.StatusError, result.status);
        Assert.Equal(ProtocolConstants.ErrorProtocolMismatch, result.error?.code);
        Assert.Equal(
            "Unity package version is incompatible with this CLI. Please upgrade both CLI binary and Unity package together.",
            result.error?.message);
        Assert.False(result.data.HasValue);
    }
}
