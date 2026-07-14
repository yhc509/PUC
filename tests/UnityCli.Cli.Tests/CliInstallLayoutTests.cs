using System.IO;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class CliInstallLayoutTests
{
    [Fact]
    public void ListInstalled_ReturnsVersionsWithExecutableAndMeta()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.3.5", "4");
        root.AddVersion("0.4.1", "5");

        var installed = CliInstallLayout.ListInstalled();

        Assert.Equal(2, installed.Count);
        Assert.Contains(installed, item => item.Version == "0.3.5" && item.ProtocolVersion == "4");
        Assert.Contains(installed, item => item.Version == "0.4.1" && item.ProtocolVersion == "5");
        Assert.All(installed, item => Assert.True(File.Exists(item.ExecutablePath)));
    }

    [Fact]
    public void ListInstalled_WhenVersionsDirectoryMissing_ReturnsEmpty()
    {
        using var root = new InstallRootScope();

        Assert.Empty(CliInstallLayout.ListInstalled());
    }

    [Fact]
    public void ListInstalled_SkipsVersionWithoutMetaJson()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.AddExecutableOnly("0.3.5");

        var installed = CliInstallLayout.ListInstalled();

        Assert.Single(installed);
        Assert.Equal("0.4.1", installed[0].Version);
    }

    [Fact]
    public void ListInstalled_SkipsVersionWithCorruptMetaJson()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.AddExecutableOnly("0.3.5");
        File.WriteAllText(CliInstallLayout.GetVersionMetaPath("0.3.5"), "{ not json");

        var installed = CliInstallLayout.ListInstalled();

        Assert.Single(installed);
        Assert.Equal("0.4.1", installed[0].Version);
    }

    [Fact]
    public void ListInstalled_SkipsVersionWhoseMetaJsonHasBlankFields()
    {
        using var root = new InstallRootScope();
        root.AddExecutableOnly("0.3.5");
        File.WriteAllText(CliInstallLayout.GetVersionMetaPath("0.3.5"), "{\"cliVersion\":\"0.3.5\"}");

        Assert.Empty(CliInstallLayout.ListInstalled());
    }

    [Fact]
    public void ListInstalled_SkipsVersionDirectoryWithoutExecutable()
    {
        using var root = new InstallRootScope();
        Directory.CreateDirectory(CliInstallLayout.GetVersionDirectory("0.3.5"));
        CliInstallLayout.WriteMeta(CliInstallLayout.GetVersionMetaPath("0.3.5"), "0.3.5", "4");

        Assert.Empty(CliInstallLayout.ListInstalled());
    }

    [Fact]
    public void ListInstalled_SkipsInstallerStagingAndBackupDirectories()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");

        // CliDownloader.ReplaceInstallDirectory stages and backs up as siblings inside versions/, and
        // the backup carries a real meta.json. A failed cleanup must not leave a phantom version.
        root.AddRawVersionDirectory(".unity-cli-install-" + Guid.NewGuid().ToString("N"), "0.3.5", "4");
        root.AddRawVersionDirectory(".unity-cli-backup-" + Guid.NewGuid().ToString("N"), "0.3.5", "4");

        var installed = CliInstallLayout.ListInstalled();

        Assert.Single(installed);
        Assert.Equal("0.4.1", installed[0].Version);
    }

    [Fact]
    public void ListInstalled_SkipsDirectoryWhoseNameDisagreesWithItsMeta()
    {
        using var root = new InstallRootScope();
        root.AddRawVersionDirectory("0.3.5", "0.4.1", "5");

        Assert.Empty(CliInstallLayout.ListInstalled());
    }

    [Fact]
    public void ListInstalled_UnparseableDirectoryDoesNotPoisonNewestWins()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.3.5", "4");
        root.AddVersion("0.4.1", "5");

        // CompareVersions returns 0 for anything it cannot parse, so a garbage version in the
        // candidate list would be unbeatable: no real version could ever compare greater than it.
        // The directory-name guard is what keeps it out of the list in the first place.
        root.AddRawVersionDirectory("garbage", "garbage", "4");

        var installed = CliInstallLayout.ListInstalled();
        Assert.Equal(2, installed.Count);
        Assert.DoesNotContain(installed, item => item.Version == "garbage");

        Assert.Equal("0.4.1", CliInstallLayout.FindNewest(installed)!.Version);
        Assert.Equal("0.3.5", CliInstallLayout.FindByProtocolVersion(installed, "4")!.Version);
    }

    [Theory]
    [InlineData("0.4.1", true)]
    [InlineData("1.0.0-rc.1", true)]
    [InlineData("v0.4.1", false)]
    [InlineData(".unity-cli-backup-abc", false)]
    [InlineData("garbage", false)]
    [InlineData("", false)]
    // RemoveVersion recursively deletes GetVersionDirectory(version), so these must never be accepted:
    // ".." resolves to the install root and "../.." escapes it entirely.
    [InlineData("..", false)]
    [InlineData("../..", false)]
    [InlineData("../../..", false)]
    [InlineData("/", false)]
    public void IsVersionDirectoryName_AcceptsOnlyNormalizedVersions(string directoryName, bool expected)
    {
        Assert.Equal(expected, CliInstallLayout.IsVersionDirectoryName(directoryName));
    }

    [Fact]
    public void FindByProtocolVersion_ReturnsMatchingProtocol()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.3.5", "4");
        root.AddVersion("0.4.1", "5");

        InstalledCliVersion? match = CliInstallLayout.FindByProtocolVersion(CliInstallLayout.ListInstalled(), "4");

        Assert.NotNull(match);
        Assert.Equal("0.3.5", match!.Version);
    }

    [Fact]
    public void FindByProtocolVersion_WhenSeveralShareProtocol_PicksNewest()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.3.4", "4");
        root.AddVersion("0.3.5", "4");
        root.AddVersion("0.2.10", "4");

        InstalledCliVersion? match = CliInstallLayout.FindByProtocolVersion(CliInstallLayout.ListInstalled(), "4");

        Assert.NotNull(match);
        Assert.Equal("0.3.5", match!.Version);
    }

    [Fact]
    public void FindByProtocolVersion_WhenNoneMatch_ReturnsNull()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");

        Assert.Null(CliInstallLayout.FindByProtocolVersion(CliInstallLayout.ListInstalled(), "4"));
    }

    [Fact]
    public void FindNewest_PicksHighestVersionRegardlessOfProtocol()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.3.5", "4");
        root.AddVersion("0.4.1", "5");
        root.AddVersion("0.10.0", "6");

        InstalledCliVersion? newest = CliInstallLayout.FindNewest(CliInstallLayout.ListInstalled());

        Assert.NotNull(newest);
        Assert.Equal("0.10.0", newest!.Version);
    }

    [Fact]
    public void WriteMeta_RoundTripsThroughTryReadMeta()
    {
        using var root = new InstallRootScope();
        string metaPath = CliInstallLayout.GetVersionMetaPath("0.4.1");

        CliInstallLayout.WriteMeta(metaPath, "v0.4.1", "5");
        CliVersionMeta? meta = CliInstallLayout.TryReadMeta(metaPath);

        Assert.NotNull(meta);
        Assert.Equal("0.4.1", meta!.cliVersion);
        Assert.Equal("5", meta.protocolVersion);
        Assert.Contains("\"cliVersion\"", File.ReadAllText(metaPath));
    }

    [Fact]
    public void TryReadMeta_WhenFileMissing_ReturnsNull()
    {
        using var root = new InstallRootScope();

        Assert.Null(CliInstallLayout.TryReadMeta(CliInstallLayout.GetVersionMetaPath("0.4.1")));
    }

    [Theory]
    [InlineData("0.4.0", "5")]
    [InlineData("0.4.1", "5")]
    [InlineData("1.0.0", "5")]
    [InlineData("0.3.5", "4")]
    [InlineData("0.1.13", "4")]
    [InlineData("0.1.12", null)]
    [InlineData("0.1.0", null)]
    [InlineData("not-a-version", null)]
    [InlineData("", null)]
    public void InferProtocolVersionForCliVersion_MatchesReleasedProtocolHistory(string cliVersion, string? expected)
    {
        Assert.Equal(expected, CliInstallLayout.InferProtocolVersionForCliVersion(cliVersion));
    }

    [Theory]
    [InlineData("0.3.5", "0.4.1", "4")]
    [InlineData("0.4.0", "0.4.1", "5")]
    [InlineData("0.4.1", "0.4.1", "5")]
    // A CLI newer than us may have bumped the protocol; the history table cannot know, so refuse.
    [InlineData("0.5.0", "0.4.1", null)]
    [InlineData("1.0.0", "0.4.1", null)]
    [InlineData("0.1.12", "0.4.1", null)]
    [InlineData("0.3.5", "not-a-version", null)]
    public void InferProtocolVersionForCliVersion_BoundedByMaxKnownVersion(
        string cliVersion,
        string maxKnownCliVersion,
        string? expected)
    {
        Assert.Equal(expected, CliInstallLayout.InferProtocolVersionForCliVersion(cliVersion, maxKnownCliVersion));
    }

    [Fact]
    public void CompareVersions_OrdersNumericCoreAndPrerelease()
    {
        Assert.True(CliInstallLayout.CompareVersions("0.4.1", "0.3.5") > 0);
        Assert.True(CliInstallLayout.CompareVersions("0.3.5", "0.3.10") < 0);
        Assert.Equal(0, CliInstallLayout.CompareVersions("v0.4.1", "0.4.1"));
        Assert.True(CliInstallLayout.CompareVersions("0.4.1-rc.1", "0.4.1") < 0);
        Assert.Equal(0, CliInstallLayout.CompareVersions("garbage", "0.4.1"));
    }

    [Fact]
    public void IsDispatchGuardSet_ReflectsEnvironmentVariable()
    {
        string? original = Environment.GetEnvironmentVariable(CliInstallLayout.DispatchGuardEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CliInstallLayout.DispatchGuardEnvironmentVariable, null);
            Assert.False(CliInstallLayout.IsDispatchGuardSet());

            Environment.SetEnvironmentVariable(CliInstallLayout.DispatchGuardEnvironmentVariable, "1");
            Assert.True(CliInstallLayout.IsDispatchGuardSet());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliInstallLayout.DispatchGuardEnvironmentVariable, original);
        }
    }

    [Fact]
    public void IsPathTargetRedundant_WhenPathTargetMatchesTheVersionItsMarkerNames_IsTrue()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetCopy("0.4.1", "5");

        Assert.True(CliInstallLayout.IsPathTargetRedundant());
        Assert.False(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    // Every PATH-target test used to model the Windows copy layout, so nothing ever exercised the
    // symlink that the Manager actually creates on the platform this ships on. That is how a guard
    // which could never fire on macOS/Linux survived three reviews.

    [Fact]
    public void IsPathTargetRedundant_WhenPathTargetIsOurSymlinkIntoVersions_IsTrue()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetSymlink("0.4.1", "5");

        Assert.True(CliInstallLayout.IsSymbolicLink(CliInstallLayout.GetPathTargetExecutablePath()));
        Assert.True(CliInstallLayout.IsPathTargetRedundant());
        Assert.False(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    [Fact]
    public void GetPathTargetReleaseAction_UnlinksOurSymlinkAndNeverQuarantinesIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetSymlink("0.4.1", "5");

        Assert.Equal(PathTargetReleaseAction.Unlink, CliInstallLayout.GetPathTargetReleaseAction());
    }

    [Fact]
    public void GetPathTargetReleaseAction_UnlinksADanglingSymlinkInsteadOfQuarantiningIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new InstallRootScope();
        root.SetDanglingPathTargetSymlink();

        // File.Exists is TRUE for a dangling symlink, so link status has to be asked separately.
        Assert.True(File.Exists(CliInstallLayout.GetPathTargetExecutablePath()));
        Assert.Equal(PathTargetReleaseAction.Unlink, CliInstallLayout.GetPathTargetReleaseAction());
        Assert.False(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    [Fact]
    public void GetPathTargetReleaseAction_QuarantinesAHandPlacedBinaryWithAStaleMarker()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetSymlink("0.4.1", "5");

        // The documented manual download extracts over the PATH directory: tar unlinks our symlink and
        // drops a real binary, leaving our marker behind to vouch for a file it knows nothing about.
        File.Delete(CliInstallLayout.GetPathTargetExecutablePath());
        root.OverwritePathTargetExecutable("someone else's 0.3.5 binary");

        Assert.Equal(PathTargetReleaseAction.Quarantine, CliInstallLayout.GetPathTargetReleaseAction());
        Assert.True(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    [Fact]
    public void GetPathTargetReleaseAction_DeletesOurWindowsStyleCopy()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetCopy("0.4.1", "5");

        Assert.Equal(PathTargetReleaseAction.Delete, CliInstallLayout.GetPathTargetReleaseAction());
    }

    [Fact]
    public void GetPathTargetReleaseAction_WhenNothingIsThere_DoesNothing()
    {
        using var root = new InstallRootScope();

        Assert.Equal(PathTargetReleaseAction.Nothing, CliInstallLayout.GetPathTargetReleaseAction());
    }

    [Fact]
    public void FilesHaveSameContent_ThroughASymlink_ComparesTheTargetsBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new InstallRootScope();
        string target = Path.Combine(root.Path, "target-with-a-long-name-so-the-path-string-differs-in-length.bin");
        File.WriteAllText(target, "seventeen bytes!!");
        string link = Path.Combine(root.Path, "link.bin");
        File.CreateSymbolicLink(link, target);

        // Regression guard: FileInfo.Length is lstat-based and reports the length of the LINK PATH
        // STRING, not the target's size. Comparing FileInfo lengths short-circuits false for every
        // symlink, which made the whole redundancy proof unable to fire on macOS/Linux.
        Assert.NotEqual(new FileInfo(link).Length, new FileInfo(target).Length);

        Assert.True(CliInstallLayout.FilesHaveSameContent(link, target));
    }

    [Fact]
    public void IsSymbolicLink_DistinguishesLinksFromRegularFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetSymlink("0.4.1", "5");

        string versionExecutablePath = CliInstallLayout.GetVersionExecutablePath("0.4.1");
        Assert.False(CliInstallLayout.IsSymbolicLink(versionExecutablePath));
        Assert.True(CliInstallLayout.IsRegularFile(versionExecutablePath));

        string pathTargetExecutablePath = CliInstallLayout.GetPathTargetExecutablePath();
        Assert.True(CliInstallLayout.IsSymbolicLink(pathTargetExecutablePath));
        Assert.False(CliInstallLayout.IsRegularFile(pathTargetExecutablePath));
    }

    [Fact]
    public void IsPathTargetRedundant_WhenAStaleMarkerSitsBesideADifferentBinary_IsFalse()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetCopy("0.4.1", "5");

        // The documented manual-download path extracts straight into the PATH directory, replacing the
        // binary but leaving our marker behind. The marker must not be able to vouch for it.
        root.OverwritePathTargetExecutable("a completely different binary");

        Assert.False(CliInstallLayout.IsPathTargetRedundant());
        Assert.True(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    [Fact]
    public void IsPathTargetRedundant_WhenThereIsNoMarker_IsFalse()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.OverwritePathTargetExecutable("legacy flat install");

        Assert.False(CliInstallLayout.IsPathTargetRedundant());
        Assert.True(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    [Fact]
    public void IsPathTargetRedundant_WhenTheVersionItsMarkerNamesIsGone_IsFalse()
    {
        using var root = new InstallRootScope();
        root.AddVersion("0.4.1", "5");
        root.SetPathTargetCopy("0.4.1", "5");
        Directory.Delete(CliInstallLayout.GetVersionDirectory("0.4.1"), recursive: true);

        Assert.False(CliInstallLayout.IsPathTargetRedundant());
    }

    [Fact]
    public void IsUnmanagedPathTargetBinaryPresent_WhenNothingIsThere_IsFalse()
    {
        using var root = new InstallRootScope();

        Assert.False(CliInstallLayout.IsUnmanagedPathTargetBinaryPresent());
    }

    [Fact]
    public void FilesHaveSameContent_ComparesLengthAndBytes()
    {
        using var root = new InstallRootScope();
        string a = Path.Combine(root.Path, "a");
        string b = Path.Combine(root.Path, "b");
        string c = Path.Combine(root.Path, "c");
        string d = Path.Combine(root.Path, "d");
        File.WriteAllText(a, "identical");
        File.WriteAllText(b, "identical");
        File.WriteAllText(c, "different");
        File.WriteAllText(d, "identical but longer");

        Assert.True(CliInstallLayout.FilesHaveSameContent(a, b));
        Assert.False(CliInstallLayout.FilesHaveSameContent(a, c));
        Assert.False(CliInstallLayout.FilesHaveSameContent(a, d));
        Assert.False(CliInstallLayout.FilesHaveSameContent(a, Path.Combine(root.Path, "missing")));
    }

    [Fact]
    public void IsInsideOrphanedDirectory_RejectsTheOrphanedRootItselfInEveryForm()
    {
        using var root = new InstallRootScope();
        string orphanedRoot = CliInstallLayout.GetOrphanedDirectory();

        // Path.GetFullPath preserves a trailing separator, so each of these once passed a naive
        // StartsWith containment check and would have recursively deleted every preserved binary.
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(orphanedRoot));
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(orphanedRoot + Path.DirectorySeparatorChar));
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(orphanedRoot + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar));
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(
            orphanedRoot + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void IsInsideOrphanedDirectory_RejectsEscapesAndSiblings()
    {
        using var root = new InstallRootScope();
        string orphanedRoot = CliInstallLayout.GetOrphanedDirectory();

        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(orphanedRoot + "-old"));
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(
            Path.Combine(orphanedRoot, "..", "versions")));
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(CliInstallLayout.GetVersionsDirectory()));
        Assert.False(CliInstallLayout.IsInsideOrphanedDirectory(string.Empty));
    }

    [Fact]
    public void IsInsideOrphanedDirectory_AcceptsProperDescendants()
    {
        using var root = new InstallRootScope();
        string orphanedRoot = CliInstallLayout.GetOrphanedDirectory();

        Assert.True(CliInstallLayout.IsInsideOrphanedDirectory(Path.Combine(orphanedRoot, "20260714-120000-abcd1234")));
        Assert.True(CliInstallLayout.IsInsideOrphanedDirectory(
            Path.Combine(orphanedRoot, "20260714-120000-abcd1234") + Path.DirectorySeparatorChar));
        Assert.True(CliInstallLayout.IsInsideOrphanedDirectory(
            Path.Combine(orphanedRoot, "20260714-120000-abcd1234", "unity-cli")));
    }

    [Fact]
    public void GetPathTargetDirectory_StaysUnderTheStableUnityCliFolder()
    {
        using var root = new InstallRootScope();

        Assert.Equal(Path.Combine(root.Path, "unity-cli"), CliInstallLayout.GetPathTargetDirectory());
        Assert.Equal(Path.Combine(root.Path, "versions"), CliInstallLayout.GetVersionsDirectory());
    }
}
