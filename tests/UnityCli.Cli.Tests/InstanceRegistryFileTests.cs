using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class InstanceRegistryFileTests
{
    [Fact]
    public async Task Load_RetriesWhileRegistryFileIsTemporarilyLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        File.WriteAllText(registryPath, BuildRegistryJson("abc"));

        using var lockStream = new FileStream(registryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var started = new ManualResetEventSlim();
        Task<InstanceRegistry> loadTask = Task.Run(() =>
        {
            started.Set();
            return InstanceRegistryFile.Load(registryPath);
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(await CompletesWithinAsync(loadTask, TimeSpan.FromMilliseconds(100)));

        lockStream.Dispose();

        InstanceRegistry registry = await loadTask;
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
    }

    [Fact]
    public async Task Load_RetriesWhileRegistryFileContainsTransientInvalidJson()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        File.WriteAllText(registryPath, "{");

        using var started = new ManualResetEventSlim();
        Task<InstanceRegistry> loadTask = Task.Run(() =>
        {
            started.Set();
            return InstanceRegistryFile.Load(registryPath);
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(await CompletesWithinAsync(loadTask, TimeSpan.FromMilliseconds(100)));

        File.WriteAllText(registryPath, BuildRegistryJson("abc"));

        InstanceRegistry registry = await loadTask;
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
    }

    [Fact]
    public async Task Save_RetriesWhileRegistryDataFileIsTemporarilyLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        File.WriteAllText(registryPath, "{\"instances\":[]}");

        using var lockStream = new FileStream(registryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var started = new ManualResetEventSlim();
        var store = new InstanceRegistryStore(registryPath);
        Task saveTask = Task.Run(() =>
        {
            started.Set();
            store.Save(CreateRegistry("abc"));
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(await CompletesWithinAsync(saveTask, TimeSpan.FromMilliseconds(100)));

        lockStream.Dispose();
        await saveTask;

        InstanceRegistry registry = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
        Assert.False(File.Exists(registryPath + ".lock"));
    }

    [Fact]
    public void Save_RecoversFromStaleLockFile()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        string lockPath = registryPath + ".lock";
        WriteStaleLockFile(lockPath, "not-a-pid");

        InstanceRegistryFile.Save(registryPath, CreateRegistry("abc"));

        InstanceRegistry registry = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void Save_RecoversFromDeadOwnerPidLock()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        string lockPath = registryPath + ".lock";
        WriteStaleLockFile(lockPath, int.MaxValue.ToString(CultureInfo.InvariantCulture));

        InstanceRegistryFile.Save(registryPath, CreateRegistry("abc"));

        InstanceRegistry registry = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task Save_PreservesLockFileWhenAcquireFails()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        string lockPath = registryPath + ".lock";
        const string marker = "99999999\n2026-05-03T00:00:00.0000000+00:00\n";

        using var lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        WriteLockContent(lockStream, marker);

        using var started = new ManualResetEventSlim();
        var store = new InstanceRegistryStore(registryPath);
        Task saveTask = Task.Run(() =>
        {
            started.Set();
            store.Save(CreateRegistry("abc"));
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(await CompletesWithinAsync(saveTask, TimeSpan.FromMilliseconds(100)));
        Assert.True(File.Exists(lockPath));
        Assert.Equal(marker, ReadLockContentForAssertion(lockPath, lockStream));

        lockStream.Dispose();
        File.Delete(lockPath);
        await saveTask;

        InstanceRegistry registry = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task Save_RetainsLiveLockEvenWhenMtimeAppearsStale()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        string lockPath = registryPath + ".lock";
        const string marker = "99999999\n2026-05-03T00:00:00.0000000+00:00\n";

        using FileStream lockStream = CreateHeldLockFileWithStaleMtime(lockPath, marker, out DateTime staleMtimeUtc);

        using var started = new ManualResetEventSlim();
        var store = new InstanceRegistryStore(registryPath);
        Task saveTask = Task.Run(() =>
        {
            started.Set();
            store.Save(CreateRegistry("abc"));
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(await CompletesWithinAsync(saveTask, TimeSpan.FromMilliseconds(250)));
        Assert.True(File.Exists(lockPath));
        Assert.Equal(marker, ReadLockContentForAssertion(lockPath, lockStream));
        Assert.Equal(staleMtimeUtc, File.GetLastWriteTimeUtc(lockPath));

        lockStream.Dispose();
        File.Delete(lockPath);
        await saveTask;

        InstanceRegistry registry = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("abc", registry.activeProjectHash);
        Assert.Single(registry.instances);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task Save_DoesNotReclaimWhenLiveOwnerStartTimeMatchesLockTimestamp()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        string lockPath = registryPath + ".lock";
        string lockContent = BuildLockContent(Process.GetCurrentProcess().Id, DateTimeOffset.UtcNow);
        File.WriteAllText(lockPath, lockContent, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddSeconds(-60));

        var store = new InstanceRegistryStore(registryPath);
        Task saveTask = Task.Run(() => store.Save(CreateRegistry("abc")));

        IOException exception = await Assert.ThrowsAsync<IOException>(() => saveTask);
        Assert.Contains(lockPath, exception.Message);
        Assert.Equal(lockContent, File.ReadAllText(lockPath, Encoding.UTF8));

        File.Delete(lockPath);
    }

    [Fact]
    public void Update_RecoversFromStaleLockFile()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        string lockPath = registryPath + ".lock";
        File.WriteAllText(registryPath, BuildRegistryJson("abc"));
        WriteStaleLockFile(lockPath, "not-a-pid");

        InstanceRegistryFile.Update(registryPath, registry =>
        {
            registry.activeProjectHash = "def";
            return registry;
        });

        InstanceRegistry updated = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("def", updated.activeProjectHash);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void Update_DeletesRegistryLockFileAfterSuccess()
    {
        using var temp = new TempDirectory();
        string registryPath = Path.Combine(temp.Path, "instances.json");
        InstanceRegistryFile.Save(registryPath, CreateRegistry("abc"));

        InstanceRegistryFile.Update(registryPath, registry =>
        {
            registry.activeProjectHash = "def";
            return registry;
        });

        InstanceRegistry updated = InstanceRegistryFile.Load(registryPath);
        Assert.Equal("def", updated.activeProjectHash);
        Assert.False(File.Exists(registryPath + ".lock"));
    }

    private static InstanceRegistry CreateRegistry(string projectHash)
    {
        return new InstanceRegistry
        {
            activeProjectHash = projectHash,
            instances =
            [
                new InstanceRecord
                {
                    projectRoot = "C:/Project",
                    projectName = "Project",
                    projectHash = projectHash,
                    pipeName = "unity-cli-" + projectHash,
                    state = "idle",
                    lastSeenUtc = "2026-04-05T00:00:00Z",
                },
            ],
        };
    }

    private static string BuildRegistryJson(string projectHash)
    {
        return "{\"activeProjectHash\":\""
            + projectHash
            + "\",\"instances\":[{\"projectRoot\":\"C:/Project\",\"projectName\":\"Project\",\"projectHash\":\""
            + projectHash
            + "\",\"pipeName\":\"unity-cli-"
            + projectHash
            + "\",\"state\":\"idle\",\"lastSeenUtc\":\"2026-04-05T00:00:00Z\"}]}";
    }

    private static void WriteStaleLockFile(string lockPath, string ownerPid)
    {
        File.WriteAllText(
            lockPath,
            ownerPid
                + Environment.NewLine
                + DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture)
                + Environment.NewLine,
            new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddSeconds(-60));
    }

    private static FileStream CreateHeldLockFileWithStaleMtime(string lockPath, string content, out DateTime staleMtimeUtc)
    {
        var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        WriteLockContent(stream, content);
        DateTime targetMtimeUtc = DateTime.UtcNow.AddSeconds(-60);

        try
        {
            File.SetLastWriteTimeUtc(lockPath, targetMtimeUtc);
        }
        catch (IOException)
        {
            stream.Dispose();
            File.SetLastWriteTimeUtc(lockPath, targetMtimeUtc);
            stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (UnauthorizedAccessException)
        {
            stream.Dispose();
            File.SetLastWriteTimeUtc(lockPath, targetMtimeUtc);
            stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        staleMtimeUtc = File.GetLastWriteTimeUtc(lockPath);
        return stream;
    }

    private static void WriteLockContent(FileStream stream, int ownerPid)
    {
        WriteLockContent(stream, BuildLockContent(ownerPid, DateTimeOffset.UtcNow));
    }

    private static void WriteLockContent(FileStream stream, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static string BuildLockContent(int ownerPid, DateTimeOffset timestamp)
    {
        return ownerPid.ToString(CultureInfo.InvariantCulture)
            + Environment.NewLine
            + timestamp.ToString("O", CultureInfo.InvariantCulture)
            + Environment.NewLine;
    }

    private static string ReadLockContentForAssertion(string lockPath, FileStream heldStream)
    {
        try
        {
            return File.ReadAllText(lockPath, Encoding.UTF8);
        }
        catch (IOException)
        {
            long position = heldStream.Position;
            heldStream.Flush();
            heldStream.Position = 0;
            using var reader = new StreamReader(heldStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            string content = reader.ReadToEnd();
            heldStream.Position = position;
            return content;
        }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        return ReferenceEquals(completedTask, task);
    }

    private static async Task<bool> CompletesWithinAsync<T>(Task<T> task, TimeSpan timeout)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        return ReferenceEquals(completedTask, task);
    }
}
