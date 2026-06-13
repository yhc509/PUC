using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class InstanceTokenSidecarTests
{
    [Fact]
    public void WriteTokenSidecar_ThenReadTokenSidecar_RoundTripsToken()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            InstanceRegistryFile.WriteTokenSidecar(registryPath, "abcdef012345", "secret-token");

            Assert.Equal("secret-token", InstanceRegistryFile.ReadTokenSidecar(registryPath, "abcdef012345"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void ReadTokenSidecar_WhenMissing_ReturnsEmpty()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            Assert.Equal(string.Empty, InstanceRegistryFile.ReadTokenSidecar(registryPath, "abcdef012345"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void WriteTokenSidecar_WhenExisting_OverwritesToken()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            InstanceRegistryFile.WriteTokenSidecar(registryPath, "abcdef012345", "old-token");
            InstanceRegistryFile.WriteTokenSidecar(registryPath, "abcdef012345", "new-token");

            Assert.Equal("new-token", InstanceRegistryFile.ReadTokenSidecar(registryPath, "abcdef012345"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void DeleteTokenSidecar_RemovesToken()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            InstanceRegistryFile.WriteTokenSidecar(registryPath, "abcdef012345", "secret-token");
            InstanceRegistryFile.DeleteTokenSidecar(registryPath, "abcdef012345");

            Assert.Equal(string.Empty, InstanceRegistryFile.ReadTokenSidecar(registryPath, "abcdef012345"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void GetTokenSidecarPath_ReturnsRegistryDirectoryTokenPath()
    {
        string registryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "instances.json");
        string expected = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(registryPath))!, "tokens", "abcdef012345.token");

        string path = InstanceRegistryFile.GetTokenSidecarPath(registryPath, "abcdef012345");

        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("09da309b95f5")]
    [InlineData("09DA309B95F5")]
    [InlineData("09da309b95f5-1")]
    [InlineData("09da309b95f5-123")]
    public void GetTokenSidecarPath_WithValidProjectHash_ReturnsTokenPath(string projectHash)
    {
        string registryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "instances.json");
        string expected = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(registryPath))!, "tokens", projectHash + ".token");

        string path = InstanceRegistryFile.GetTokenSidecarPath(registryPath, projectHash);

        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("09da309b95f5/../x")]
    [InlineData("zzzz")]
    [InlineData("zzzzzzzzzzzz")]
    [InlineData("09da309b95f5-")]
    [InlineData("09da309b95f5-x")]
    [InlineData("09da309b95f")]
    [InlineData("09da309b95f55")]
    public void GetTokenSidecarPath_WithInvalidProjectHash_ReturnsEmpty(string projectHash)
    {
        string registryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "instances.json");

        string path = InstanceRegistryFile.GetTokenSidecarPath(registryPath, projectHash);

        Assert.Equal(string.Empty, path);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("09da309b95f5/../x")]
    [InlineData("zzzz")]
    [InlineData("09da309b95f5-")]
    [InlineData("09da309b95f5-x")]
    [InlineData("09da309b95f")]
    [InlineData("09da309b95f55")]
    public void TokenSidecar_WithInvalidProjectHash_NoOpsAndReturnsEmpty(string projectHash)
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            InstanceRegistryFile.WriteTokenSidecar(registryPath, projectHash, "secret-token");

            Assert.Equal(string.Empty, InstanceRegistryFile.ReadTokenSidecar(registryPath, projectHash));
            Assert.False(Directory.Exists(tempRoot));
            Assert.False(Directory.Exists(Path.Combine(tempRoot, "tokens")));
            Assert.False(File.Exists(Path.Combine(tempRoot, "x.token")));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void WriteTokenSidecar_WithTraversalProjectHash_DoesNotWriteOutsideTokenDirectory()
    {
        string tempRoot = CreateTempRoot();
        string outsideName = Guid.NewGuid().ToString("N");
        string projectHash = "../../" + outsideName;
        string outsidePath = Path.GetFullPath(Path.Combine(tempRoot, "tokens", projectHash + ".token"));
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            InstanceRegistryFile.WriteTokenSidecar(registryPath, projectHash, "secret-token");

            Assert.Equal(string.Empty, InstanceRegistryFile.ReadTokenSidecar(registryPath, projectHash));
            Assert.False(File.Exists(outsidePath));
            Assert.False(Directory.Exists(tempRoot));
        }
        finally
        {
            if (File.Exists(outsidePath))
            {
                File.Delete(outsidePath);
            }

            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void WriteTokenSidecar_OnUnix_AppliesOwnerOnlyModes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");
            string sidecarPath = InstanceRegistryFile.GetTokenSidecarPath(registryPath, "abcdef012345");
            string tokenDirectory = Path.GetDirectoryName(sidecarPath)!;

            InstanceRegistryFile.WriteTokenSidecar(registryPath, "abcdef012345", "secret-token");

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(sidecarPath));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(tokenDirectory));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void TokenSidecar_WithEmptyProjectHash_NoOpsAndReturnsEmpty()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");

            InstanceRegistryFile.WriteTokenSidecar(registryPath, string.Empty, "secret-token");
            InstanceRegistryFile.DeleteTokenSidecar(registryPath, string.Empty);

            Assert.Equal(string.Empty, InstanceRegistryFile.GetTokenSidecarPath(registryPath, string.Empty));
            Assert.Equal(string.Empty, InstanceRegistryFile.ReadTokenSidecar(registryPath, string.Empty));
            Assert.False(Directory.Exists(Path.Combine(tempRoot, "tokens")));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void Save_ThenLoad_DoesNotPersistInstanceToken()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string registryPath = Path.Combine(tempRoot, "instances.json");
            var registry = new InstanceRegistry
            {
                activeProjectRoot = "/tmp/project",
                instances =
                [
                    new InstanceRecord
                    {
                        projectRoot = "/tmp/project",
                        projectName = "project",
                        projectHash = "abcdef012345",
                        pipeName = "/tmp/unity-cli-abcdef012345.sock",
                        token = "secret-token",
                        state = "idle",
                        lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                        capabilities = [],
                    },
                ],
            };

            InstanceRegistryFile.Save(registryPath, registry);
            string json = File.ReadAllText(registryPath);
            InstanceRegistry loaded = InstanceRegistryFile.Load(registryPath);

            Assert.DoesNotContain("\"token\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Single(loaded.instances);
            Assert.Equal(string.Empty, loaded.instances[0].token);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
