using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class PackageListFilterUtilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyPackageListFilter_EmptyOrWhitespaceFilterReturnsAllSortedByName(string filter)
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = filter, limit = 0 });

        Assert.Equal(
            ["com.alpha.render", "com.unity.textmeshpro", "com.zeta.tools", "org.sample.case"],
            result.Select(record => record.name).ToArray());
    }

    [Fact]
    public void ApplyPackageListFilter_MissingFilterReturnsEmptyArray()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = "not-installed", limit = 0 });

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyPackageListFilter_MatchesNameSubstring()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = "textmesh", limit = 0 });

        Assert.Equal(["com.unity.textmeshpro"], result.Select(record => record.name).ToArray());
    }

    [Fact]
    public void ApplyPackageListFilter_MatchesDisplayNameSubstring()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = "renderer", limit = 0 });

        Assert.Equal(["com.alpha.render"], result.Select(record => record.name).ToArray());
    }

    [Fact]
    public void ApplyPackageListFilter_MatchesCaseInsensitive()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = "mixed case", limit = 0 });

        Assert.Equal(["org.sample.case"], result.Select(record => record.name).ToArray());
    }

    [Fact]
    public void ApplyPackageListFilter_PositiveLimitAppliesAfterSorting()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = string.Empty, limit = 2 });

        Assert.Equal(["com.alpha.render", "com.unity.textmeshpro"], result.Select(record => record.name).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ApplyPackageListFilter_ZeroOrNegativeLimitIsUnlimited(int limit)
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = string.Empty, limit = limit });

        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void ApplyPackageListFilter_FilterAndLimitApplyInOrder()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            UnsortedRecords(),
            new PackageListArgs { filter = "com.", limit = 2 });

        Assert.Equal(["com.alpha.render", "com.unity.textmeshpro"], result.Select(record => record.name).ToArray());
    }

    [Fact]
    public void ApplyPackageListFilter_SortsByNameRegardlessOfInputOrder()
    {
        PackageRecord[] result = PackageListFilterUtility.ApplyPackageListFilter(
            [
                Record("org.third", "Third"),
                Record("com.second", "Second"),
                Record("com.first", "First"),
            ],
            new PackageListArgs { filter = string.Empty, limit = 0 });

        Assert.Equal(["com.first", "com.second", "org.third"], result.Select(record => record.name).ToArray());
    }

    private static PackageRecord[] UnsortedRecords()
    {
        return
        [
            Record("com.zeta.tools", "Zeta Tools"),
            Record("com.unity.textmeshpro", "TextMesh Pro"),
            Record("com.alpha.render", "Alpha Renderer"),
            Record("org.sample.case", "Mixed CASE Display"),
        ];
    }

    private static PackageRecord Record(string name, string displayName)
    {
        return new PackageRecord
        {
            name = name,
            version = "1.0.0",
            displayName = displayName,
            source = "Registry",
        };
    }
}
