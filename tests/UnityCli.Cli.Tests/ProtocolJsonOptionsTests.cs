using System.Reflection;
using System.Text;
using System.Text.Json;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolJsonOptionsTests
{
    [Fact]
    public void Default_UsesExplicitMaxDepth()
    {
        Assert.Equal(128, ProtocolJson.Default.MaxDepth);
    }

    [Fact]
    public void EnsureData_ParsesDepth64Payload()
    {
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            dataJson: BuildNestedObject(depth: 64),
            durationMs: 1);

        response.EnsureData();

        Assert.IsType<JsonElement>(response.data);
    }

    [Fact]
    public void EnsureData_ThrowsJsonExceptionWhenPayloadExceedsMaxDepth()
    {
        var response = ResponseEnvelope.Success(
            requestId: "req-1",
            target: "target-1",
            dataJson: BuildNestedObject(depth: 130),
            durationMs: 1);

        Assert.Throws<JsonException>(() => response.EnsureData());
        Assert.Null(response.data);
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
}
