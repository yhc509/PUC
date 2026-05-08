using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ExecuteCodeHandlerTests
{
    [Fact]
    public void ToCSharpStringLiteral_EscapesLineAndParagraphSeparators()
    {
        string literal = ExecuteCodeStringLiteral.ToCSharpStringLiteral("a\u2028b\u2029c");

        Assert.Equal("\"a\\u2028b\\u2029c\"", literal);
        Assert.DoesNotContain('\u2028', literal);
        Assert.DoesNotContain('\u2029', literal);
    }

    [Fact]
    public void ToCSharpStringLiteral_EscapesBom()
    {
        string literal = ExecuteCodeStringLiteral.ToCSharpStringLiteral("a\ufeffb");

        Assert.Equal("\"a\\ufeffb\"", literal);
        Assert.DoesNotContain('\ufeff', literal);
    }

    [Fact]
    public void ToCSharpStringLiteral_EscapesSurrogatePairAsUtf16Chars()
    {
        string literal = ExecuteCodeStringLiteral.ToCSharpStringLiteral(char.ConvertFromUtf32(0x1f600));

        Assert.Equal("\"\\ud83d\\ude00\"", literal);
    }

    [Fact]
    public void ToCSharpStringLiteral_EscapesEmbeddedQuotesAndBackslashes()
    {
        const string json = "{\"k\":\"a\\\"b\\\\c\"}";

        string literal = ExecuteCodeStringLiteral.ToCSharpStringLiteral(json);

        Assert.Equal("\"{\\\"k\\\":\\\"a\\\\\\\"b\\\\\\\\c\\\"}\"", literal);
    }

    [Fact]
    public void ToCSharpStringLiteral_KeepsPrintableAsciiRaw()
    {
        const string ascii = "AZaz09 !#$%&'()*+,-./:;<=>?@[]^_`{|}~";

        string literal = ExecuteCodeStringLiteral.ToCSharpStringLiteral(ascii);

        Assert.Equal("\"" + ascii + "\"", literal);
    }

    [Fact]
    public void ToCSharpStringLiteral_OutputUsesOnlyAsciiAndValidEscapes()
    {
        string value = "a\u2028b\u2029\ufeff"
            + char.ConvertFromUtf32(0x1f600)
            + "{\"k\":\"a\\\"b\\\\c\"}"
            + "\u007f\u0001";

        string literal = ExecuteCodeStringLiteral.ToCSharpStringLiteral(value);

        Assert.All(literal, character => Assert.InRange(character, '\u0020', '\u007e'));
        AssertValidCSharpEscapeSequences(literal);
    }

    [Fact]
    public void ExecuteCodeArgs_TimeoutMs_RoundTripsThroughJson()
    {
        var args = new ExecuteCodeArgs
        {
            code = "// noop",
            force = true,
            timeoutMs = 60000,
        };

        string json = ProtocolJson.Serialize(args);
        var deserialized = ProtocolJson.Deserialize<ExecuteCodeArgs>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(60000, deserialized!.timeoutMs);
    }

    [Fact]
    public void ExecuteCodeArgs_TimeoutMs_DefaultsToNull_WhenOmitted()
    {
        var deserialized = ProtocolJson.Deserialize<ExecuteCodeArgs>("{\"code\":\"x\",\"force\":true}");

        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.timeoutMs);
    }

    [Fact]
    public void ProtocolConstants_ExecuteTimeoutDefaultsAndBounds()
    {
        Assert.Equal(30_000, ProtocolConstants.DefaultExecuteTimeoutMs);
        Assert.Equal(600_000, ProtocolConstants.MaxExecuteTimeoutMs);
        Assert.Equal("EXECUTE_TIMEOUT", ProtocolConstants.ErrorExecuteTimeout);
    }

    private static void AssertValidCSharpEscapeSequences(string literal)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            if (literal[index] != '\\')
            {
                continue;
            }

            Assert.True(index + 1 < literal.Length, "Backslash cannot terminate a C# string literal.");
            char escape = literal[++index];
            if (escape == 'u')
            {
                Assert.True(index + 4 < literal.Length, "\\u escape requires four hex digits.");
                Assert.True(IsHex(literal[index + 1]), "\\u escape requires four hex digits.");
                Assert.True(IsHex(literal[index + 2]), "\\u escape requires four hex digits.");
                Assert.True(IsHex(literal[index + 3]), "\\u escape requires four hex digits.");
                Assert.True(IsHex(literal[index + 4]), "\\u escape requires four hex digits.");
                index += 4;
                continue;
            }

            Assert.True(IsSimpleEscape(escape), "Unexpected C# string escape sequence: \\" + escape);
        }
    }

    private static bool IsSimpleEscape(char character)
    {
        return character == '\\'
            || character == '"'
            || character == '0'
            || character == 'r'
            || character == 'n'
            || character == 't';
    }

    private static bool IsHex(char character)
    {
        return (character >= '0' && character <= '9')
            || (character >= 'a' && character <= 'f')
            || (character >= 'A' && character <= 'F');
    }
}
