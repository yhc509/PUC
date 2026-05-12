#nullable enable
using System;
using System.IO;

namespace UnityCliBridge.Bridge.Editor
{
#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
    internal static class UnixSocketFileUtility
    {
        internal static bool TryCleanupDeadSocketFile(string pipeName, Action<string>? liveSocketWarning = null)
        {
            if (Path.DirectorySeparatorChar == '\\' || string.IsNullOrWhiteSpace(pipeName) || !File.Exists(pipeName))
            {
                return true;
            }

            if (UnixSocketProbe.IsLiveUnixSocket(pipeName))
            {
                liveSocketWarning?.Invoke(pipeName);
                return false;
            }

            try
            {
                File.Delete(pipeName);
            }
            catch
            {
            }

            return !File.Exists(pipeName);
        }
    }
#endif
}
