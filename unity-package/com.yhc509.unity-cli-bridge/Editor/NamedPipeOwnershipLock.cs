#nullable enable
using System;
using System.IO;
using System.Text;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed class NamedPipeOwnershipLock : IDisposable
    {
        private readonly FileStream _stream;
        private readonly string _lockPath;
        private bool _disposed;

        private NamedPipeOwnershipLock(FileStream stream, string lockPath)
        {
            _stream = stream;
            _lockPath = lockPath;
        }

        internal static bool TryAcquire(string pipeName, out NamedPipeOwnershipLock? ownershipLock)
        {
            ownershipLock = null;

            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return false;
            }

            string lockPath = GetLockPath(pipeName);
            string? directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                ownershipLock = new NamedPipeOwnershipLock(stream, lockPath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static string GetLockPath(string pipeName)
        {
            string root = Path.Combine(Path.GetTempPath(), "unity-cli-bridge");
            return Path.Combine(root, SanitizeFileName(pipeName) + ".lock");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();

            try
            {
                File.Delete(_lockPath);
            }
            catch
            {
            }
        }

        private static string SanitizeFileName(string value)
        {
            var builder = new StringBuilder(value.Length);
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                builder.Append(Array.IndexOf(invalidCharacters, character) >= 0
                    || character == Path.DirectorySeparatorChar
                    || character == Path.AltDirectorySeparatorChar
                        ? '_'
                        : character);
            }

            return builder.ToString();
        }
    }
}
