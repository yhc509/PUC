#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
#if !UNITY_5_3_OR_NEWER
using System.Text.Json;
#endif

namespace UnityCli.Protocol
{
    public static class InstanceRegistryFile
    {
        private const int MaxRetryCount = 10;
        private const int RetryDelayMs = 30;
        private const int StaleLockSeconds = 30;

        public static InstanceRegistry Load(string filePath)
        {
            string fullPath = EnsureDirectory(filePath);
            Exception? lastException = null;

            for (int attempt = 0; attempt < MaxRetryCount; attempt++)
            {
                try
                {
                    return LoadUnlocked(fullPath);
                }
                catch (IOException exception)
                {
                    lastException = exception;
                    Thread.Sleep((attempt + 1) * RetryDelayMs);
                }
                catch (Exception exception) when (IsJsonParseException(exception))
                {
                    lastException = exception;
                    Thread.Sleep((attempt + 1) * RetryDelayMs);
                }
            }

            if (lastException != null)
            {
                throw lastException;
            }

            return new InstanceRegistry();
        }

        public static void Save(string filePath, InstanceRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException("registry");
            }

            string fullPath = EnsureDirectory(filePath);
            WithExclusiveLock(fullPath, () => WriteAtomically(fullPath, NormalizeRegistry(registry)));
        }

        public static void Update(string filePath, Func<InstanceRegistry, InstanceRegistry> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException("update");
            }

            string fullPath = EnsureDirectory(filePath);
            WithExclusiveLock(fullPath, () =>
            {
                InstanceRegistry current = LoadUnlocked(fullPath);
                InstanceRegistry next = NormalizeRegistry(update(current));
                WriteAtomically(fullPath, next);
            });
        }

        private static void WithExclusiveLock(string fullPath, Action action)
        {
            string lockPath = fullPath + ".lock";
            IOException? lastException = null;

            for (int attempt = 0; attempt < MaxRetryCount; attempt++)
            {
                FileStream? lockStream;
                bool acquired = TryAcquireLock(lockPath, out lockStream);

                if (acquired && lockStream != null)
                {
                    try
                    {
                        action();
                        return;
                    }
                    catch (IOException exception)
                    {
                        lastException = exception;
                    }
                    finally
                    {
                        ReleaseLock(lockStream, lockPath);
                    }
                }
                else
                {
                    lastException = new IOException("Failed to acquire registry lock: " + lockPath);
                    if (IsStaleLock(lockPath))
                    {
                        TryDeleteFile(lockPath);
                    }
                }

                Thread.Sleep((attempt + 1) * RetryDelayMs);
            }

            if (lastException != null)
            {
                throw lastException;
            }
        }

        private static bool TryAcquireLock(string lockPath, out FileStream? lockStream)
        {
            FileStream? stream = null;
            bool acquired = false;

            try
            {
                stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                acquired = true;
                // Unity Mono runtime lacks Environment.ProcessId (.NET 5+ only); Process.GetCurrentProcess().Id stays portable.
                int processId = Process.GetCurrentProcess().Id;
                string content = processId.ToString(CultureInfo.InvariantCulture)
                    + Environment.NewLine
                    + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + Environment.NewLine;
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                lockStream = stream;
                return true;
            }
            catch (IOException)
            {
                if (acquired && stream != null)
                {
                    ReleaseLock(stream, lockPath);
                }

                lockStream = null;
                return false;
            }
        }

        private static bool IsStaleLock(string lockPath)
        {
            DateTime lastWriteTimeUtc;

            try
            {
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(lockPath);
            }
            catch (IOException)
            {
                return false;
            }

            if (DateTime.UtcNow - lastWriteTimeUtc < TimeSpan.FromSeconds(StaleLockSeconds))
            {
                return false;
            }

            try
            {
                string[] lines = File.ReadAllLines(lockPath);
                if (lines.Length == 0 || !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
                {
                    return true;
                }

                using Process process = Process.GetProcessById(processId);
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private static void ReleaseLock(FileStream lockStream, string lockPath)
        {
            try
            {
                lockStream.Dispose();
            }
            finally
            {
                TryDeleteFile(lockPath);
            }
        }

        private static void WriteAtomically(string fullPath, InstanceRegistry registry)
        {
            string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(ProtocolJson.Serialize(registry));
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, null);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static InstanceRegistry LoadUnlocked(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return new InstanceRegistry();
            }

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string json = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new InstanceRegistry();
            }

            return NormalizeRegistry(ProtocolJson.Deserialize<InstanceRegistry>(json));
        }

        private static InstanceRegistry NormalizeRegistry(InstanceRegistry? registry)
        {
            if (registry == null)
            {
                registry = new InstanceRegistry();
            }

            if (registry.instances == null)
            {
                registry.instances = Array.Empty<InstanceRecord>();
            }

            return registry;
        }

        private static string EnsureDirectory(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return fullPath;
        }

        private static bool IsJsonParseException(Exception exception)
        {
#if UNITY_5_3_OR_NEWER
            return exception is ArgumentException;
#else
            return exception is JsonException;
#endif
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
