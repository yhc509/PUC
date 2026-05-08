#nullable enable
using System.IO;
using System.Net.Sockets;

namespace UnityCliBridge.Bridge.Editor
{
#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
    internal static class UnixSocketProbe
    {
        internal static bool IsLiveUnixSocket(string path)
        {
            if (Path.DirectorySeparatorChar == '\\' || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                try
                {
                    socket.Connect(new UnixDomainSocketEndPoint(path));
                    return true;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }
    }
#endif
}
