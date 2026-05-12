using UnityCliBridge.Bridge.Editor;

namespace UnityCli.Cli.Tests;

public sealed class NamedPipeOwnershipLockTests
{
    [Fact]
    public void TryAcquire_WhenLockIsFree_ReturnsOwnedLock()
    {
        string pipeName = CreatePipeName();
        string lockPath = NamedPipeOwnershipLock.GetLockPath(pipeName);

        try
        {
            var acquired = NamedPipeOwnershipLock.TryAcquire(pipeName, out var ownershipLock);

            Assert.True(acquired);
            Assert.NotNull(ownershipLock);
            Assert.True(File.Exists(lockPath));

            ownershipLock.Dispose();
            Assert.False(File.Exists(lockPath));
        }
        finally
        {
            DeleteLockFile(lockPath);
        }
    }

    [Fact]
    public void TryAcquire_WhenLockIsHeld_ReturnsFalse()
    {
        string pipeName = CreatePipeName();
        string lockPath = NamedPipeOwnershipLock.GetLockPath(pipeName);

        try
        {
            Assert.True(NamedPipeOwnershipLock.TryAcquire(pipeName, out var firstLock));
            using (firstLock)
            {
                Assert.False(NamedPipeOwnershipLock.TryAcquire(pipeName, out var secondLock));
                Assert.Null(secondLock);
            }
        }
        finally
        {
            DeleteLockFile(lockPath);
        }
    }

    private static string CreatePipeName()
    {
        return "unity-cli-test-" + Guid.NewGuid().ToString("N");
    }

    private static void DeleteLockFile(string lockPath)
    {
        if (File.Exists(lockPath))
        {
            File.Delete(lockPath);
        }
    }
}
