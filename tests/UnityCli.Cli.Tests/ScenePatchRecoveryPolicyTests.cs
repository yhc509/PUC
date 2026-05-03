using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ScenePatchRecoveryPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ShouldReloadAfterFailedPatch_ReloadsOnlyWhenTargetWasLoaded(bool targetWasLoaded, bool expected)
    {
        Assert.Equal(expected, ScenePatchRecoveryPolicy.ShouldReloadAfterFailedPatch(targetWasLoaded));
    }
}
