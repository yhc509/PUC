using UnityCli.DocGen;

namespace UnityCli.Cli.Tests;

public sealed class TestRunnerCachePolicyTests
{
    [Fact]
    public void TestRunnerCallbacks_UsesAtomicWritesForResultAndLastRunPointer()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory),
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            "TestRunnerCallbacks.cs"));

        Assert.Contains("AtomicFileUtility.WriteAllText(finalPath", source);
        Assert.Contains("AtomicFileUtility.WriteAllText(lastRunPath", source);
        Assert.DoesNotContain("File.Delete(lastRunPath", source);
        Assert.DoesNotContain("File.Delete(finalPath", source);
    }

    [Fact]
    public void TestCommandHandler_CleansStaleTestRunTempFilesDuringRestore()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory),
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            "TestCommandHandler.cs"));

        Assert.Contains("CleanupStaleTestRunTempFiles();", source);
        Assert.Contains("AtomicFileUtility.CleanupTempFiles(runsDir);", source);
    }
}
