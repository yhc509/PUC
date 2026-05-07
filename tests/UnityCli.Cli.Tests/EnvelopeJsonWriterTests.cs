using UnityCli.Protocol;
using UnityCliBridge.Bridge.Editor;

namespace UnityCli.Cli.Tests;

public sealed class EnvelopeJsonWriterTests
{
    [Fact]
    public void Write_EmitsErrorDetailsAsInlineJsonObject()
    {
        var envelope = NewErrorEnvelope(details: "{\"path\":\"/Root[0]\",\"reason\":\"missing\"}");

        var json = EnvelopeJsonWriter.Write(envelope);

        Assert.Contains("\"details\":{\"path\":\"/Root[0]\",\"reason\":\"missing\"}", json);
        Assert.DoesNotContain("\"details\":\"{", json);
    }

    [Fact]
    public void Write_EmitsErrorDetailsAsInlineJsonString_WhenFreeformText()
    {
        var envelope = NewErrorEnvelope(details: "\"plain text\"");

        var json = EnvelopeJsonWriter.Write(envelope);

        Assert.Contains("\"details\":\"plain text\"", json);
    }

    [Fact]
    public void ValidateRawJson_ThrowsOnInvalidFragment()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => EnvelopeJsonWriter.ValidateRawJson("{not valid json"));

        Assert.Equal("Response envelope JSON fragment is not valid.", exception.Message);
    }

    private static ResponseEnvelope NewErrorEnvelope(string details)
    {
        return ResponseEnvelope.Failure(
            requestId: "r1",
            target: null,
            code: "E_X",
            message: "boom",
            retryable: false,
            transport: ProtocolConstants.TransportLive,
            details: details);
    }
}
