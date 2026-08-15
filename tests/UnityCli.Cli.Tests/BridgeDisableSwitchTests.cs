using UnityCli.DocGen;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

/// <summary>
/// The opt-out switch a CI/release build job uses to keep the bridge from booting. Parsing is
/// covered directly; the ordering guard below is the part that cannot be unit-tested, because the
/// switch is only worth anything if it runs before the host is constructed.
/// </summary>
public sealed class BridgeDisableSwitchTests
{
    private static readonly string[] NoArgs = Array.Empty<string>();

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData(" 1 ")]
    public void IsDisabled_WhenEnvironmentValueIsSet_DisablesTheBridge(string value)
    {
        Assert.True(BridgeDisableSwitch.IsDisabled(value, NoArgs));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("False")]
    public void IsDisabled_WhenEnvironmentValueMeansOff_LeavesTheBridgeRunning(string? value)
    {
        Assert.False(BridgeDisableSwitch.IsDisabled(value!, NoArgs));
    }

    [Fact]
    public void IsDisabled_WhenCommandLineFlagIsPresent_DisablesTheBridge()
    {
        string[] args = { "/Applications/Unity", "-batchmode", "-noUnityCliBridge", "-quit" };

        Assert.True(BridgeDisableSwitch.IsDisabled(null!, args));
    }

    [Fact]
    public void IsDisabled_WhenCommandLineFlagCasingDiffers_StillDisablesTheBridge()
    {
        string[] args = { "-nounityclibridge" };

        Assert.True(BridgeDisableSwitch.IsDisabled(null!, args));
    }

    [Fact]
    public void IsDisabled_WhenAnUnrelatedFlagIsPresent_LeavesTheBridgeRunning()
    {
        string[] args = { "-batchmode", "-nographics", "-quit" };

        Assert.False(BridgeDisableSwitch.IsDisabled(null!, args));
    }

    [Fact]
    public void IsDisabled_WhenArgumentsAreNull_DoesNotThrow()
    {
        Assert.False(BridgeDisableSwitch.IsDisabled(null!, null!));
    }

    [Fact]
    public void Bootstrap_ChecksTheSwitchBeforeConstructingTheHost()
    {
        string repoRoot = RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            "BridgeHost.cs"));

        int switchIndex = source.IndexOf("BridgeDisableSwitch.IsDisabled(", StringComparison.Ordinal);
        int constructionIndex = source.IndexOf("new BridgeHost()", StringComparison.Ordinal);

        Assert.True(switchIndex >= 0, "BridgeHost.cs must consult BridgeDisableSwitch.");
        Assert.True(constructionIndex >= 0, "BridgeHost.cs must still construct the host.");
        Assert.True(
            switchIndex < constructionIndex,
            "The disable switch must be evaluated before the host is constructed.");
    }
}
