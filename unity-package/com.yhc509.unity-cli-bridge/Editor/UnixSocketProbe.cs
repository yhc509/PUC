#nullable enable
using System.IO;
using System.Net.Sockets;

namespace UnityCliBridge.Bridge.Editor
{
#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
    internal static class UnixSocketProbe
    {
        private const int ConnectTimeoutMilliseconds = 50;

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
                    socket.Blocking = false;
                    socket.Connect(new UnixDomainSocketEndPoint(path));
                    return true;
                }
                catch (SocketException exception) when (IsConnectionInProgress(exception))
                {
                    if (!socket.Poll(ConnectTimeoutMilliseconds * 1000, SelectMode.SelectWrite))
                    {
                        return true;
                    }

                    object? errorValue = socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                    return errorValue is int error && error == 0;
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

        private static bool IsConnectionInProgress(SocketException exception)
        {
            return exception.SocketErrorCode == SocketError.WouldBlock
                || exception.SocketErrorCode == SocketError.InProgress
                || exception.SocketErrorCode == SocketError.AlreadyInProgress;
        }
    }
#endif
}
