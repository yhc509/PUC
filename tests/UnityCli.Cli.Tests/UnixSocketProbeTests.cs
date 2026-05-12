using System.Net.Sockets;
using UnityCliBridge.Bridge.Editor;

namespace UnityCli.Cli.Tests;

public sealed class UnixSocketProbeTests
{
    [Fact]
    public void IsLiveUnixSocket_MissingSocketFile_ReturnsFalse()
    {
        string path = GetSocketPath();

        Assert.False(UnixSocketProbe.IsLiveUnixSocket(path));
    }

    [Fact]
    public void IsLiveUnixSocket_LiveListener_ReturnsTrue()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string path = GetSocketPath();
        Socket? listener = null;

        try
        {
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);

            Assert.True(UnixSocketProbe.IsLiveUnixSocket(path));
        }
        finally
        {
            listener?.Dispose();
            DeleteSocketFile(path);
        }
    }

    [Fact]
    public void IsLiveUnixSocket_ExistingDeadSocketPath_ReturnsFalse()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string path = GetSocketPath();

        try
        {
            File.WriteAllText(path, string.Empty);

            Assert.True(File.Exists(path));
            Assert.False(UnixSocketProbe.IsLiveUnixSocket(path));
        }
        finally
        {
            DeleteSocketFile(path);
        }
    }

    private static string GetSocketPath()
    {
        string root = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        return Path.Combine(root, "ucb-" + Guid.NewGuid().ToString("N") + ".sock");
    }

    private static void DeleteSocketFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
