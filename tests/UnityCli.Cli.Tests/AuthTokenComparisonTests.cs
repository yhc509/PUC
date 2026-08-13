using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class AuthTokenComparisonTests
{
    [Fact]
    public void FixedTimeEquals_WhenIdentical_ReturnsTrue()
    {
        string token = new string('a', 64);

        Assert.True(AuthTokenComparison.FixedTimeEquals(token, new string('a', 64)));
    }

    [Fact]
    public void FixedTimeEquals_WhenLastCharacterDiffers_ReturnsFalse()
    {
        string expected = new string('a', 64);
        string candidate = expected.Substring(0, 63) + "b";

        Assert.False(AuthTokenComparison.FixedTimeEquals(expected, candidate));
    }

    [Fact]
    public void FixedTimeEquals_WhenFirstCharacterDiffers_ReturnsFalse()
    {
        string expected = new string('a', 64);
        string candidate = "b" + expected.Substring(1);

        Assert.False(AuthTokenComparison.FixedTimeEquals(expected, candidate));
    }

    [Fact]
    public void FixedTimeEquals_IsCaseSensitive()
    {
        Assert.False(AuthTokenComparison.FixedTimeEquals("abcdef", "ABCDEF"));
    }

    [Theory]
    [InlineData("token", "")]
    [InlineData("", "token")]
    [InlineData("", "")]
    [InlineData("token", null)]
    [InlineData(null, "token")]
    [InlineData(null, null)]
    public void FixedTimeEquals_WhenEitherSideIsMissing_ReturnsFalse(string? expected, string? candidate)
    {
        Assert.False(AuthTokenComparison.FixedTimeEquals(expected!, candidate!));
    }

    [Theory]
    [InlineData("abcdef", "abcde")]
    [InlineData("abcde", "abcdef")]
    public void FixedTimeEquals_WhenPrefixMatchesButLengthDiffers_ReturnsFalse(string expected, string candidate)
    {
        Assert.False(AuthTokenComparison.FixedTimeEquals(expected, candidate));
    }
}
