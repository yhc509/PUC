using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProfileStatMathTests
{
    [Fact]
    public void Median_OddCount_ReturnsMiddle()
    {
        Assert.Equal(3.0, ProfileStatMath.Median(new[] { 1.0, 3.0, 9.0 }));
    }

    [Fact]
    public void Median_EvenCount_ReturnsAverageOfMiddleTwo()
    {
        Assert.Equal(2.5, ProfileStatMath.Median(new[] { 1.0, 2.0, 3.0, 4.0 }));
    }

    [Fact]
    public void Median_Empty_ReturnsZero()
    {
        Assert.Equal(0.0, ProfileStatMath.Median(Array.Empty<double>()));
    }

    [Fact]
    public void Percentile95_NearestRank()
    {
        double[] sorted = Enumerable.Range(1, 100).Select(v => (double)v).ToArray();
        Assert.Equal(95.0, ProfileStatMath.Percentile(sorted, 95));
    }

    [Fact]
    public void Percentile_SingleElement_ReturnsIt()
    {
        Assert.Equal(7.0, ProfileStatMath.Percentile(new[] { 7.0 }, 95));
    }

    [Fact]
    public void BuildFrameTimeStats_FindsWorstFrameAndOverBudget()
    {
        double[] frames = { 10.0, 41.2, 12.0, 18.0 };
        ProfileFrameTimeStats stats = ProfileStatMath.BuildFrameTimeStats(frames, 16.67f);

        Assert.Equal(41.2, stats.worstMs);
        Assert.Equal(1, stats.worstFrame);
        Assert.Equal(2, stats.overBudgetCount);
        Assert.Equal(15.0, stats.medianMs);
    }

    [Fact]
    public void BuildFrameTimeStats_Empty_ReturnsDefaults()
    {
        ProfileFrameTimeStats stats = ProfileStatMath.BuildFrameTimeStats(Array.Empty<double>(), 16.67f);
        Assert.Equal(-1, stats.worstFrame);
        Assert.Equal(0, stats.overBudgetCount);
    }

    [Fact]
    public void ComputeVerdict_NoOverBudget_IsWithinBudget()
    {
        ProfileVerdict verdict = ProfileStatMath.ComputeVerdict(20.0, 10.0, sawGpuWaitMarker: true, overBudgetCount: 0);
        Assert.Equal("withinBudget", verdict.bound);
        Assert.Equal("gpuTime", verdict.basis);
        Assert.Equal(20.0, verdict.gpuMedianMs);
    }

    [Fact]
    public void ComputeVerdict_GpuTimeDominates_IsGpuBound()
    {
        ProfileVerdict verdict = ProfileStatMath.ComputeVerdict(30.0, 20.0, sawGpuWaitMarker: false, overBudgetCount: 5);
        Assert.Equal("gpu", verdict.bound);
        Assert.Equal("gpuTime", verdict.basis);
    }

    [Fact]
    public void ComputeVerdict_NoGpuTimeButWaitMarker_IsGpuBoundByWaitMarkers()
    {
        ProfileVerdict verdict = ProfileStatMath.ComputeVerdict(0.0, 20.0, sawGpuWaitMarker: true, overBudgetCount: 5);
        Assert.Equal("gpu", verdict.bound);
        Assert.Equal("waitMarkers", verdict.basis);
        Assert.Equal(-1, verdict.gpuMedianMs);
    }

    [Fact]
    public void ComputeVerdict_NoGpuSignal_IsCpuBound()
    {
        ProfileVerdict verdict = ProfileStatMath.ComputeVerdict(0.0, 20.0, sawGpuWaitMarker: false, overBudgetCount: 5);
        Assert.Equal("cpu", verdict.bound);
        Assert.Equal("none", verdict.basis);
    }
}
