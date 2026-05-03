using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class InstanceRegistryStoreTests
{
    [Fact]
    public void ResolveOrCreateTarget_CreatesOfflineEntryFromProjectPath()
    {
        using var temp = new TempDirectory();
        var projectRoot = System.IO.Path.Combine(temp.Path, "ProjectA");
        Directory.CreateDirectory(projectRoot);

        var store = new InstanceRegistryStore(System.IO.Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry();
        var target = store.ResolveOrCreateTarget(registry, projectRoot);

        Assert.Equal(ProtocolConstants.GetCanonicalPath(projectRoot), target.projectRoot);
        Assert.Single(registry.instances);
        Assert.Equal(target.projectHash, registry.instances[0].projectHash);
        Assert.Equal(ProtocolConstants.BuildPipeName(target.projectHash), target.pipeName);
    }

    [Fact]
    public void ResolveOrCreateTarget_UsesRegisteredProjectName()
    {
        using var temp = new TempDirectory();
        var projectRoot = Path.Combine(temp.Path, "ProjectA");
        Directory.CreateDirectory(projectRoot);
        var projectHash = ProtocolConstants.ComputeProjectHash(projectRoot);

        var store = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry
        {
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = projectRoot,
                    projectName = "UnityCliBridge",
                    projectHash = projectHash,
                    pipeName = ProtocolConstants.BuildPipeName(projectHash),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
            ],
        };

        var target = store.ResolveOrCreateTarget(registry, "unityclibridge");

        Assert.Equal(ProtocolConstants.GetCanonicalPath(projectRoot), target.projectRoot);
        Assert.Equal(projectHash, target.projectHash);
        Assert.Single(registry.instances);
    }

    [Fact]
    public void ResolveProjectRootOverride_ReturnsCanonicalRegisteredProjectPath()
    {
        using var temp = new TempDirectory();
        var projectRoot = Path.Combine(temp.Path, "ProjectA");
        Directory.CreateDirectory(projectRoot);

        var store = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry
        {
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = projectRoot,
                    projectName = "UnityCliBridge",
                    projectHash = ProtocolConstants.ComputeProjectHash(projectRoot),
                    pipeName = ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(projectRoot)),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
            ],
        };

        var resolved = store.ResolveProjectRootOverride(registry, "unityclibridge");

        Assert.Equal(ProtocolConstants.GetCanonicalPath(projectRoot), resolved);
    }

    [Fact]
    public void ResolveProjectRootOverride_PathTakesPrecedenceOverRegisteredProjectName()
    {
        using var temp = new TempDirectory();
        var pathProjectRoot = Path.Combine(temp.Path, "UnityCliBridge");
        var registeredProjectRoot = Path.Combine(temp.Path, "RegisteredProject");
        Directory.CreateDirectory(pathProjectRoot);
        Directory.CreateDirectory(registeredProjectRoot);

        var store = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry
        {
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = registeredProjectRoot,
                    projectName = "UnityCliBridge",
                    projectHash = ProtocolConstants.ComputeProjectHash(registeredProjectRoot),
                    pipeName = ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(registeredProjectRoot)),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
            ],
        };

        string originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = temp.Path;
            var resolved = store.ResolveProjectRootOverride(registry, "UnityCliBridge");

            Assert.Equal(ProtocolConstants.GetCanonicalPath(pathProjectRoot), resolved);
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Fact]
    public void ResolveProjectRootOverride_WhenMultipleProjectsMatch_ThrowsUsageException()
    {
        using var temp = new TempDirectory();
        var firstProjectRoot = Path.Combine(temp.Path, "ProjectA");
        var secondProjectRoot = Path.Combine(temp.Path, "ProjectB");
        Directory.CreateDirectory(firstProjectRoot);
        Directory.CreateDirectory(secondProjectRoot);

        var store = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry
        {
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = firstProjectRoot,
                    projectName = "UnityCliBridge",
                    projectHash = ProtocolConstants.ComputeProjectHash(firstProjectRoot),
                    pipeName = ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(firstProjectRoot)),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
                new InstanceRecord
                {
                    projectRoot = secondProjectRoot,
                    projectName = "unityclibridge",
                    projectHash = ProtocolConstants.ComputeProjectHash(secondProjectRoot),
                    pipeName = ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(secondProjectRoot)),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
            ],
        };

        var exception = Assert.Throws<CliUsageException>(() => store.ResolveProjectRootOverride(registry, "UnityCliBridge"));

        Assert.Contains("중복되어", exception.Message);
        Assert.Contains(ProtocolConstants.GetCanonicalPath(firstProjectRoot), exception.Message);
        Assert.Contains(ProtocolConstants.GetCanonicalPath(secondProjectRoot), exception.Message);
    }

    [Fact]
    public void ResolveProjectRootOverride_WhenProjectNameDoesNotMatch_ThrowsUsageException()
    {
        using var temp = new TempDirectory();
        var store = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry();

        var exception = Assert.Throws<CliUsageException>(() => store.ResolveProjectRootOverride(registry, "UnityCliBridge"));

        Assert.Equal(
            "'UnityCliBridge' is not a registered project name or a valid directory path. Run 'unity-cli instances list' to see registered projects.",
            exception.Message);
    }

    [Fact]
    public void ResolveOrCreateTarget_WhenProjectNameDoesNotMatch_ThrowsUsageException()
    {
        using var temp = new TempDirectory();
        var store = new InstanceRegistryStore(Path.Combine(temp.Path, "instances.json"));
        var registry = new InstanceRegistry();

        var exception = Assert.Throws<CliUsageException>(() => store.ResolveOrCreateTarget(registry, "TypoProject"));

        Assert.Equal(
            "'TypoProject' is not a known project hash, a registered project name, or a valid directory path. Run 'unity-cli instances list' to see registered projects.",
            exception.Message);
    }

    [Fact]
    public void Load_RemovesMissingProjectsAndPromotesLiveInstance()
    {
        using var temp = new TempDirectory();
        var existingProject = Path.Combine(temp.Path, "ProjectA");
        Directory.CreateDirectory(existingProject);

        var registryPath = Path.Combine(temp.Path, "instances.json");
        var store = new InstanceRegistryStore(registryPath);
        store.Save(new InstanceRegistry
        {
            activeProjectHash = "missing",
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = Path.Combine(temp.Path, "MissingProject"),
                    projectName = "MissingProject",
                    projectHash = "missing",
                    pipeName = "missing.sock",
                    state = "offline",
                    lastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"),
                },
                new InstanceRecord
                {
                    projectRoot = existingProject,
                    projectName = "ProjectA",
                    projectHash = ProtocolConstants.ComputeProjectHash(existingProject),
                    pipeName = ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(existingProject)),
                    state = "idle",
                    lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
            ],
        });

        var registry = store.Load();

        Assert.Single(registry.instances);
        Assert.Equal("ProjectA", registry.instances[0].projectName);
        Assert.Equal(registry.instances[0].projectHash, registry.activeProjectHash);
    }

    [Fact]
    public void IsStale_WithUnparseableLastSeenAndNoProcess_ReturnsTrue()
    {
        var record = new InstanceRecord
        {
            lastSeenUtc = "garbage",
            editorProcessId = 0,
        };

        Assert.True(InstanceRegistryStore.IsStale(record));
    }

    [Fact]
    public void IsStale_WithUnparseableLastSeenAndLiveProcess_ReturnsFalse()
    {
        var record = new InstanceRecord
        {
            lastSeenUtc = "garbage",
            editorProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
        };

        Assert.False(InstanceRegistryStore.IsStale(record));
    }

    [Fact]
    public void IsStale_WithOldLastSeenAndNoProcess_ReturnsTrue()
    {
        var record = new InstanceRecord
        {
            lastSeenUtc = DateTimeOffset.UtcNow.AddHours(-1).ToString("O"),
            editorProcessId = 0,
        };

        Assert.True(InstanceRegistryStore.IsStale(record));
    }

    [Fact]
    public void IsStale_WithRecentLastSeenAndNoProcess_ReturnsFalse()
    {
        var record = new InstanceRecord
        {
            lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
            editorProcessId = 0,
        };

        Assert.False(InstanceRegistryStore.IsStale(record));
    }
}
