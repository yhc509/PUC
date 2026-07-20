using UnityCliBridge.Bridge.Editor;

namespace UnityCli.Cli.Tests;

public sealed class CliInstallGuardTests
{
    private const string RealReleaseUrl =
        "https://github.com/yhc509/unity-cli-bridge/releases/download/v0.4.1/unity-cli-osx-arm64.tar.gz";

    // -------------------------------------------------------------------------
    // EnsureNoSymlinks
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsureNoSymlinks_WhenStagingHoldsPlainExecutable_Accepts()
    {
        using var temp = new TempDirectory();
        string staging = CreateStaging(temp);

        CliInstallGuard.EnsureNoSymlinks(staging);
    }

    [Fact]
    public void EnsureNoSymlinks_WhenStagingIsEmpty_Accepts()
    {
        using var temp = new TempDirectory();
        string staging = Path.Combine(temp.Path, "staging");
        Directory.CreateDirectory(staging);

        CliInstallGuard.EnsureNoSymlinks(staging);
    }

    [Fact]
    public void EnsureNoSymlinks_WhenExecutableIsSymlinkedFile_Rejects()
    {
        using var temp = new TempDirectory();
        string staging = CreateStaging(temp);

        string outsideFile = Path.Combine(temp.Path, "outside-secret.txt");
        File.WriteAllText(outsideFile, "sensitive");

        if (!TryCreateFileSymbolicLink(Path.Combine(staging, "unity-cli-link"), outsideFile))
        {
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => CliInstallGuard.EnsureNoSymlinks(staging));

        Assert.Contains("symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unity-cli-link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureNoSymlinks_WhenStagingHoldsSymlinkedDirectory_RejectsWithoutWalkingTheTarget()
    {
        using var temp = new TempDirectory();
        string staging = CreateStaging(temp);

        // Stands in for a sensitive directory such as ~/.ssh.
        string outsideDirectory = Path.Combine(temp.Path, "outside-tree");
        Directory.CreateDirectory(outsideDirectory);
        const string DistinctiveFileName = "DISTINCTIVE-SECRET.txt";
        File.WriteAllText(Path.Combine(outsideDirectory, DistinctiveFileName), "id_rsa");

        string linkPath = Path.Combine(staging, "linked-dir");
        if (!TryCreateDirectorySymbolicLink(linkPath, outsideDirectory))
        {
            return;
        }

        // The vector this guard exists for: the enumeration CopyDirectoryContents uses does
        // follow the directory symlink and reports the file outside staging. If this ever
        // stops holding, the one-level-at-a-time walk below is no longer load-bearing.
        string[] naivelyEnumerated = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
        Assert.Contains(
            naivelyEnumerated,
            path => path.EndsWith(DistinctiveFileName, StringComparison.Ordinal));

        var exception = Assert.Throws<InvalidOperationException>(
            () => CliInstallGuard.EnsureNoSymlinks(staging));

        Assert.Contains("linked-dir", exception.Message, StringComparison.Ordinal);
        // The walk stopped at the link; nothing behind it was ever enumerated.
        Assert.DoesNotContain(DistinctiveFileName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureNoSymlinks_WhenNestedSubdirectoryHoldsSymlink_Rejects()
    {
        using var temp = new TempDirectory();
        string staging = CreateStaging(temp);

        string nested = Path.Combine(staging, "lib", "deep");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "plain.txt"), "plain");

        string outsideFile = Path.Combine(temp.Path, "outside-secret.txt");
        File.WriteAllText(outsideFile, "sensitive");

        if (!TryCreateFileSymbolicLink(Path.Combine(nested, "nested-link"), outsideFile))
        {
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => CliInstallGuard.EnsureNoSymlinks(staging));

        Assert.Contains("nested-link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureNoSymlinks_WhenSymlinkTargetIsMissing_Rejects()
    {
        using var temp = new TempDirectory();
        string staging = CreateStaging(temp);

        string danglingTarget = Path.Combine(temp.Path, "never-created.txt");
        if (!TryCreateFileSymbolicLink(Path.Combine(staging, "dangling-link"), danglingTarget))
        {
            return;
        }

        // Both .NET and Unity's Mono report ReparsePoint for a dangling link rather than
        // throwing, so this is the guard's own rejection, not an incidental failure.
        var exception = Assert.Throws<InvalidOperationException>(
            () => CliInstallGuard.EnsureNoSymlinks(staging));

        Assert.Contains("symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dangling-link", exception.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // EnsureTrustedDownloadUrl
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsureTrustedDownloadUrl_WhenUrlIsRealReleaseAsset_Accepts()
    {
        CliInstallGuard.EnsureTrustedDownloadUrl(RealReleaseUrl);
    }

    [Fact]
    public void EnsureTrustedDownloadUrl_WhenHostCasingDiffers_Accepts()
    {
        CliInstallGuard.EnsureTrustedDownloadUrl(
            "https://GitHub.COM/yhc509/unity-cli-bridge/releases/download/v0.4.1/unity-cli-win-x64.zip");
    }

    [Fact]
    public void EnsureTrustedDownloadUrl_WhenPortIsExplicitHttpsDefault_Accepts()
    {
        CliInstallGuard.EnsureTrustedDownloadUrl(
            "https://github.com:443/yhc509/unity-cli-bridge/releases/download/v0.4.1/unity-cli-osx-arm64.tar.gz");
    }

    [Theory]
    // Plaintext transport.
    [InlineData("http://github.com/yhc509/unity-cli-bridge/releases/download/v0.4.1/unity-cli-osx-arm64.tar.gz")]
    // Lookalike host: defeats a naive "starts with https://github.com" prefix compare.
    [InlineData("https://github.com.evil.test/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz")]
    // Trusted host relegated to a path segment on an attacker host.
    [InlineData("https://evil.test/github.com/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz")]
    // Credentials trick: the real host here is evil.test, "github.com" is only userinfo.
    [InlineData("https://github.com@evil.test/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz")]
    [InlineData("https://user:password@github.com/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz")]
    // Non-default port.
    [InlineData("https://github.com:8443/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz")]
    // Outside /releases/download/.
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/tag/v0.4.1")]
    [InlineData("https://github.com/yhc509/unity-cli-bridge/archive/refs/heads/main.tar.gz")]
    // Adjacent repository whose name merely starts with the trusted one.
    [InlineData("https://github.com/yhc509/unity-cli-bridge-evil/releases/download/v0.4.1/x.tar.gz")]
    // Segment that merely starts with "download".
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/downloadX/v0.4.1/x.tar.gz")]
    // Traversal. Raw and "%2e"-encoded forms normalise out of the trusted prefix; the
    // "%2f"-encoded form does not — Uri leaves it alone — and is caught by the
    // percent-sign rule instead.
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/download/../../../evil/x.tar.gz")]
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/download/%2e%2e/%2e%2e/evil.tar.gz")]
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/download/..%2f..%2f..%2fevil/x.tar.gz")]
    // The parsed Uri and the raw string the caller actually downloads must agree. These
    // escape to "%0D%0A" / "%00" during parsing and would otherwise read as a clean path
    // while real control bytes reached the web request.
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz\r\nX-Injected: 1")]
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz\0.evil")]
    // The prefix itself, with no asset named after it.
    [InlineData("https://github.com/yhc509/unity-cli-bridge/releases/download/")]
    // Not absolute / not a URL / not http(s).
    [InlineData("/yhc509/unity-cli-bridge/releases/download/v0.4.1/x.tar.gz")]
    [InlineData("releases/download/v0.4.1/x.tar.gz")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://github.com\\@evil.test/yhc509/unity-cli-bridge/releases/download/v1/x.tar.gz")]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureTrustedDownloadUrl_WhenUrlIsNotAReleaseAsset_Rejects(string url)
    {
        Assert.Throws<InvalidOperationException>(() => CliInstallGuard.EnsureTrustedDownloadUrl(url));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string CreateStaging(TempDirectory temp)
    {
        string staging = Path.Combine(temp.Path, "staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "unity-cli"), "binary");
        return staging;
    }

    /// <summary>
    /// Returns false when the environment forbids symlink creation (notably Windows without
    /// developer mode), so the caller can skip rather than fail.
    /// </summary>
    private static bool TryCreateFileSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
