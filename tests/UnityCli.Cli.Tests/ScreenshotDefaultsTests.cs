using UnityCli.Protocol;
using Xunit;

namespace UnityCli.Cli.Tests;

public class ScreenshotDefaultsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveFormat_WithNoRequestAndNoPath_DefaultsToJpg(string? requestedFormat)
    {
        Assert.True(ScreenshotDefaults.TryResolveFormat(requestedFormat!, null!, out string format));
        Assert.Equal(ScreenshotDefaults.FormatJpg, format);
    }

    [Theory]
    [InlineData("/tmp/shot.png")]
    [InlineData("/tmp/shot.PNG")]
    [InlineData("  /tmp/nested.dir/shot.Png  ")]
    public void TryResolveFormat_WithPngOutputPath_KeepsPng(string outputPath)
    {
        // Writing JPEG bytes into a file the caller named .png would be a worse outcome than the
        // tokens the default saves, so an explicit extension outranks the default.
        Assert.True(ScreenshotDefaults.TryResolveFormat(null!, outputPath, out string format));
        Assert.Equal(ScreenshotDefaults.FormatPng, format);
    }

    [Theory]
    [InlineData("/tmp/shot.jpg")]
    [InlineData("/tmp/shot.jpeg")]
    [InlineData("/tmp/shot.webp")]
    [InlineData("/tmp/shot")]
    [InlineData(".png")]
    public void TryResolveFormat_WithNonPngOutputPath_UsesTheJpgDefault(string outputPath)
    {
        Assert.True(ScreenshotDefaults.TryResolveFormat(null!, outputPath, out string format));
        Assert.Equal(ScreenshotDefaults.FormatJpg, format);
    }

    [Theory]
    [InlineData("png", "png")]
    [InlineData("PNG", "png")]
    [InlineData("  jpg  ", "jpg")]
    [InlineData("jpeg", "jpg")]
    [InlineData("JPEG", "jpg")]
    public void TryResolveFormat_WithExplicitFormat_OverridesTheOutputExtension(string requested, string expected)
    {
        Assert.True(ScreenshotDefaults.TryResolveFormat(requested, "/tmp/shot.png", out string format));
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("jpgg")]
    public void TryResolveFormat_WithUnknownFormat_Fails(string requested)
    {
        Assert.False(ScreenshotDefaults.TryResolveFormat(requested, null!, out _));
    }

    [Fact]
    public void ResolveMaxWidth_WithNothingSpecified_AppliesTheDefaultCap()
    {
        Assert.Equal(ScreenshotDefaults.DefaultMaxWidth, ScreenshotDefaults.ResolveMaxWidth(0, 0, 0));
    }

    [Fact]
    public void ResolveMaxWidth_WithExplicitCap_UsesIt()
    {
        Assert.Equal(640, ScreenshotDefaults.ResolveMaxWidth(640, 0, 0));
    }

    [Fact]
    public void ResolveMaxWidth_WithUncappedSentinel_DisablesTheCap()
    {
        Assert.Equal(0, ScreenshotDefaults.ResolveMaxWidth(ScreenshotDefaults.MaxWidthUncapped, 0, 0));
    }

    [Theory]
    [InlineData(1920, 0)]
    [InlineData(0, 1080)]
    [InlineData(1920, 1080)]
    public void ResolveMaxWidth_WithAnExplicitSize_LeavesTheSizeAlone(int width, int height)
    {
        // The explicit-size gate predates the default cap; keeping it is what stops this change
        // from silently shrinking captures that already state the size they want.
        Assert.Equal(0, ScreenshotDefaults.ResolveMaxWidth(0, width, height));
        Assert.Equal(0, ScreenshotDefaults.ResolveMaxWidth(512, width, height));
    }

    [Theory]
    [InlineData("jpg", ".jpg")]
    [InlineData("png", ".png")]
    public void FileExtension_MatchesTheResolvedFormat(string format, string expected)
    {
        Assert.Equal(expected, ScreenshotDefaults.FileExtension(format));
    }
}
