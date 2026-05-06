using System.Text.Json;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ResponseEnvelopeTests
{
    [Fact]
    public void Success_WithData_AssignsJsonElement()
    {
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            data: ParseData("{\"message\":\"hello\"}"),
            durationMs: 12);

        var data = AssertData(response);
        Assert.Equal("hello", data.GetProperty("message").GetString());
    }

    [Fact]
    public void Success_WithNullData_LeavesDataNull()
    {
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            data: null,
            durationMs: 12);

        Assert.False(response.data.HasValue);
    }

    [Fact]
    public void Success_SetsProtocolVersionAndRoundTrips()
    {
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            data: ParseData("{\"message\":\"hello\"}"),
            durationMs: 12);

        var json = ProtocolJson.Serialize(response);
        var roundTrip = ProtocolJson.Deserialize<ResponseEnvelope>(json);

        Assert.Equal(ProtocolConstants.ProtocolVersion, response.protocolVersion);
        Assert.Equal(ProtocolConstants.ProtocolVersion, roundTrip.protocolVersion);
        Assert.Contains("\"protocolVersion\":\"2\"", json);
    }

    [Fact]
    public void Deserialize_WithBridgeWireData_PopulatesData()
    {
        var response = ProtocolJson.Deserialize<ResponseEnvelope>(
            "{\"requestId\":\"req-1\",\"protocolVersion\":\"2\",\"target\":\"target-1\",\"status\":\"success\",\"durationMs\":12,\"data\":{\"message\":\"hello\"},\"retryable\":false,\"transport\":\"live\"}");

        var data = AssertData(response);
        Assert.Equal("hello", data.GetProperty("message").GetString());
    }

    [Fact]
    public void Success_WithMutationWarnings_PreservesWarningsArray()
    {
        var payload = new PrefabMutationPayload
        {
            patched = false,
            warnings = ["Unknown key: m_LocalScal.x"],
        };
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            data: JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            durationMs: 12);

        var data = AssertData(response);
        Assert.False(data.GetProperty("patched").GetBoolean());
        Assert.Equal("Unknown key: m_LocalScal.x", data.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void Success_WithScreenshotPayload_PreservesCoordinateMetadata()
    {
        var payload = new ScreenshotPayload
        {
            savedPath = "/tmp/shot.png",
            width = 960,
            height = 540,
            screenWidth = 1920,
            screenHeight = 1080,
            coordinateOrigin = "bottom-left",
            imageOrigin = "top-left",
            fileSizeBytes = 1234,
        };
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            data: JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            durationMs: 12);

        var data = AssertData(response);
        Assert.Equal("/tmp/shot.png", data.GetProperty("savedPath").GetString());
        Assert.Equal(960, data.GetProperty("width").GetInt32());
        Assert.Equal(540, data.GetProperty("height").GetInt32());
        Assert.Equal(1920, data.GetProperty("screenWidth").GetInt32());
        Assert.Equal(1080, data.GetProperty("screenHeight").GetInt32());
        Assert.Equal("bottom-left", data.GetProperty("coordinateOrigin").GetString());
        Assert.Equal("top-left", data.GetProperty("imageOrigin").GetString());
        Assert.Equal(1234, data.GetProperty("fileSizeBytes").GetInt64());
    }

    private static JsonElement ParseData(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json, ProtocolJson.Default);
    }

    private static JsonElement AssertData(ResponseEnvelope response)
    {
        Assert.True(response.data.HasValue);
        return response.data.Value;
    }
}
