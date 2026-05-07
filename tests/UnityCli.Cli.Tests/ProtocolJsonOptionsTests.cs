using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolJsonOptionsTests
{
    private const string UnicodeEscapePattern = @"\\u[0-9a-fA-F]{4}";

    [Fact]
    public void Default_UsesExplicitMaxDepth()
    {
        Assert.Equal(128, ProtocolJson.Default.MaxDepth);
    }

    [Fact]
    public void Default_UsesUnsafeRelaxedJsonEscapingEncoder()
    {
        Assert.Same(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, ProtocolJson.Default.Encoder);
    }

    [Fact]
    public void Default_SerializesKoreanTextWithoutUnicodeEscapes()
    {
        var json = ProtocolJson.Serialize("안녕하세요");

        Assert.Contains("안녕하세요", json);
        Assert.DoesNotMatch(UnicodeEscapePattern, json);
    }

    [Fact]
    public void Default_SerializingJsonElementParsedFromEscapedKorean_WritesLiteralKorean()
    {
        using var document = JsonDocument.Parse("{\"message\":\"\\uC548\\uB155\"}");
        JsonElement payload = document.RootElement.Clone();

        var json = ProtocolJson.Serialize(new { data = payload });

        Assert.Contains("안녕", json);
        Assert.DoesNotMatch(UnicodeEscapePattern, json);
    }

    [Fact]
    public void Default_DeserializesDepth64DataPayload()
    {
        var response = ProtocolJson.Deserialize<ResponseEnvelope>(
            BuildEnvelopeWithData(BuildNestedObject(depth: 63)));

        Assert.True(response.data.HasValue);
    }

    [Fact]
    // Depth 64 becomes wire depth 65 once wrapped in the envelope, which default STJ 64 would reject.
    public void Default_DeserializesWireDepthAboveStjDefault()
    {
        var response = ProtocolJson.Deserialize<ResponseEnvelope>(
            BuildEnvelopeWithData(BuildNestedObject(depth: 64)));

        Assert.True(response.data.HasValue);
    }

    [Fact]
    public void Default_ThrowsJsonExceptionWhenDataPayloadExceedsMaxDepth()
    {
        Assert.Throws<JsonException>(() =>
            ProtocolJson.Deserialize<ResponseEnvelope>(BuildEnvelopeWithData(BuildNestedObject(depth: 128))));
    }

    [Fact]
    public void ResponseFormatter_PrintOptionsUseProtocolMaxDepth()
    {
        var optionFields = typeof(ResponseFormatter)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(JsonSerializerOptions))
            .ToArray();

        Assert.Collection(
            optionFields.OrderBy(field => field.Name),
            field => Assert.Equal("CompactPrintOptions", field.Name),
            field => Assert.Equal("PrettyPrintOptions", field.Name));

        foreach (FieldInfo field in optionFields)
        {
            var options = Assert.IsType<JsonSerializerOptions>(field.GetValue(null));
            Assert.Equal(ProtocolJson.Default.MaxDepth, options.MaxDepth);
            Assert.Same(ProtocolJson.Default.Encoder, options.Encoder);
        }
    }

    [Fact]
    public void ResponseFormatter_PrintOptionsSerializeKoreanTextWithoutUnicodeEscapes()
    {
        var optionFields = typeof(ResponseFormatter)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(JsonSerializerOptions))
            .ToArray();

        foreach (FieldInfo field in optionFields)
        {
            var options = Assert.IsType<JsonSerializerOptions>(field.GetValue(null));
            var json = JsonSerializer.Serialize(new { message = "안녕하세요" }, options);

            Assert.Contains("안녕하세요", json);
            Assert.DoesNotMatch(UnicodeEscapePattern, json);
        }
    }

    private static string BuildNestedObject(int depth)
    {
        var builder = new StringBuilder((depth * 8) + 2);
        for (int index = 0; index < depth; index++)
        {
            builder.Append("{\"child\":");
        }

        builder.Append("null");

        for (int index = 0; index < depth; index++)
        {
            builder.Append('}');
        }

        return builder.ToString();
    }

    private static string BuildEnvelopeWithData(string dataFragment)
    {
        return "{\"requestId\":\"req-1\",\"status\":\"success\",\"durationMs\":1,\"data\":"
            + dataFragment
            + ",\"retryable\":false,\"transport\":\"live\"}";
    }
}
