using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class QaCoordinateConverterTests
{
    [Fact]
    public void ConvertScreenshotXToScreenX_WithoutScreenshotWidth_ReturnsRawX()
    {
        int convertedX = QaCoordinateConverter.ConvertScreenshotXToScreenX(rawX: 300, screenWidth: 960, screenshotWidth: 0);

        Assert.Equal(300, convertedX);
    }

    [Fact]
    public void ConvertScreenshotYToScreenY_WithoutScreenshotHeight_ReturnsRawY()
    {
        int convertedY = QaCoordinateConverter.ConvertScreenshotYToScreenY(rawY: 200, screenHeight: 1080, screenshotHeight: 0);

        Assert.Equal(200, convertedY);
    }

    [Fact]
    public void ConvertScreenshotYToScreenY_WithNonPositiveScreenHeight_ReturnsRawY()
    {
        int convertedY = QaCoordinateConverter.ConvertScreenshotYToScreenY(rawY: 200, screenHeight: 0, screenshotHeight: 1080);

        Assert.Equal(200, convertedY);
    }

    [Fact]
    public void ConvertScreenshotCoordinates_WithScreenshotDimensions_ScalesAndInvertsY()
    {
        int convertedX = QaCoordinateConverter.ConvertScreenshotXToScreenX(rawX: 300, screenWidth: 960, screenshotWidth: 1920);
        int convertedY = QaCoordinateConverter.ConvertScreenshotYToScreenY(rawY: 200, screenHeight: 540, screenshotHeight: 1080);

        Assert.Equal(150, convertedX);
        Assert.Equal(440, convertedY);
    }

    [Fact]
    public void ConvertScreenshotCoordinates_WhenScreenMatchesScreenshot_IsIdentityWithYFlip()
    {
        int convertedX = QaCoordinateConverter.ConvertScreenshotXToScreenX(rawX: 845, screenWidth: 1440, screenshotWidth: 1440);
        int convertedY = QaCoordinateConverter.ConvertScreenshotYToScreenY(rawY: 2540, screenHeight: 2960, screenshotHeight: 2960);

        Assert.Equal(845, convertedX);
        Assert.Equal(420, convertedY);
    }

    [Fact]
    public void IsAspectMismatch_WhenAspectsMatch_ReturnsFalse()
    {
        Assert.False(QaCoordinateConverter.IsAspectMismatch(1440, 2960, 1440, 2960));
        Assert.False(QaCoordinateConverter.IsAspectMismatch(720, 1480, 1440, 2960));
    }

    [Fact]
    public void IsAspectMismatch_WhenAspectsDiffer_ReturnsTrue()
    {
        Assert.True(QaCoordinateConverter.IsAspectMismatch(1440, 2960, 2490, 2674));
    }

    [Fact]
    public void IsAspectMismatch_WithNonPositiveDimensions_ReturnsFalse()
    {
        Assert.False(QaCoordinateConverter.IsAspectMismatch(0, 2960, 1440, 2960));
    }
}
