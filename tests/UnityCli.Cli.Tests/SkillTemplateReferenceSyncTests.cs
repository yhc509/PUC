using UnityCli.DocGen;

namespace UnityCli.Cli.Tests;

public sealed class SkillTemplateReferenceSyncTests
{
    [Fact]
    public void MaintainerAndShippedSkillReferences_AreIdentical()
    {
        string repoRoot = RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory);
        string maintainerRoot = Path.Combine(repoRoot, "tools", "skills", "unity-cli-operator", "references");
        string shippedRoot = Path.Combine(
            repoRoot,
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "SkillTemplates~",
            "references");

        Assert.True(Directory.Exists(maintainerRoot), "Maintainer skill references are missing: " + maintainerRoot);
        Assert.True(Directory.Exists(shippedRoot), "Shipped skill references are missing: " + shippedRoot);
        Assert.Equal(ReadTree(maintainerRoot), ReadTree(shippedRoot));
    }

    private static Dictionary<string, string> ReadTree(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.ReadAllText(path).Replace("\r\n", "\n"),
                StringComparer.Ordinal);
    }
}
