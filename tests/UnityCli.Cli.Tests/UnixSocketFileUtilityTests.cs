using System.Net.Sockets;
using UnityCli.Protocol;
using UnityCliBridge.Bridge.Editor;

namespace UnityCli.Cli.Tests;

public sealed class UnixSocketFileUtilityTests
{
    [Fact]
    public void TryCleanupDeadSocketFile_WhenPathHasLiveListener_PreservesSocketFileAndWarns()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string path = GetSocketPath();
        Socket? listener = null;
        var warnings = new List<string>();

        try
        {
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);

            var cleaned = UnixSocketFileUtility.TryCleanupDeadSocketFile(path, warnings.Add);

            Assert.False(cleaned);
            Assert.True(File.Exists(path));
            Assert.Equal([path], warnings);
        }
        finally
        {
            listener?.Dispose();
            DeleteSocketFile(path);
        }
    }

    [Fact]
    public void TryCleanupDeadSocketFile_WhenSuffixedPathIsDead_RemovesSocketFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string baseHash = Guid.NewGuid().ToString("N")[..12];
        string path = ProtocolConstants.BuildPipeName(baseHash + "-1");

        try
        {
            File.WriteAllText(path, string.Empty);

            var cleaned = UnixSocketFileUtility.TryCleanupDeadSocketFile(path);

            Assert.True(cleaned);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteSocketFile(path);
        }
    }

    private static string GetSocketPath()
    {
        return Path.Combine("/tmp", "ucb-" + Guid.NewGuid().ToString("N") + ".sock");
    }

    private static void DeleteSocketFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
