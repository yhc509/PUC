#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace UnityCli.Protocol
{
    public sealed class FileBackupTransactionException : Exception
    {
        public FileBackupTransactionException(string errorCode, string message, string? details = null, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            Details = details;
        }

        public string ErrorCode { get; }
        public string? Details { get; }
    }

    public sealed class FileBackupTransactionOptions
    {
        public Func<string>? BackupIdFactory { get; set; }
        public Action<string>? WarningSink { get; set; }
        public Action? Refresh { get; set; }
    }

    public static class FileBackupTransaction
    {
        private const string BackupToken = ".bridge-bak.";

        private enum BackupMode
        {
            Copy,
            Move,
        }

        public static T RunWithBackup<T>(string path, string commandName, Func<T> action, FileBackupTransactionOptions? options = null)
        {
            return Run(path, commandName, action, BackupMode.Copy, options);
        }

        public static T RunWithMovedBackup<T>(string path, string commandName, Func<T> action, FileBackupTransactionOptions? options = null)
        {
            return Run(path, commandName, action, BackupMode.Move, options);
        }

        public static string BuildBackupPath(string path, string backupId)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string fileName = Path.GetFileName(path);
            return Path.Combine(directory, "." + fileName + BackupToken + backupId);
        }

        private static T Run<T>(string path, string commandName, Func<T> action, BackupMode mode, FileBackupTransactionOptions? options)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("백업 대상 path가 비어 있습니다.", nameof(path));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            options ??= new FileBackupTransactionOptions();
            string backupId = CreateBackupId(options);
            BackupEntry[] entries = BuildEntries(path, backupId);
            var backedUpEntries = new List<BackupEntry>(entries.Length);

            try
            {
                for (int index = 0; index < entries.Length; index++)
                {
                    BackupEntry entry = entries[index];
                    if (!entry.WasPresent)
                    {
                        continue;
                    }

                    CreateBackup(entry, mode, options);
                    backedUpEntries.Add(entry);
                }

                if (mode == BackupMode.Move && backedUpEntries.Count > 0)
                {
                    options.Refresh?.Invoke();
                }
            }
            catch (Exception exception)
            {
                CleanupBackups(backedUpEntries, options);
                options.Refresh?.Invoke();
                throw CreateBackupFailedException(path, commandName, entries, exception);
            }

            try
            {
                T result = action();
                CleanupBackups(backedUpEntries, options);
                if (mode == BackupMode.Move && backedUpEntries.Count > 0)
                {
                    options.Refresh?.Invoke();
                }

                return result;
            }
            catch (Exception actionException)
            {
                try
                {
                    RestoreEntries(entries, mode);
                    CleanupBackups(backedUpEntries, options);
                    options.Refresh?.Invoke();
                }
                catch (Exception restoreException)
                {
                    options.Refresh?.Invoke();
                    throw CreateRestoreFailedException(path, commandName, backedUpEntries, actionException, restoreException);
                }

                throw;
            }
        }

        private static string CreateBackupId(FileBackupTransactionOptions options)
        {
            string? requestedId = options.BackupIdFactory == null ? null : options.BackupIdFactory();
            if (!string.IsNullOrWhiteSpace(requestedId))
            {
                return requestedId.Trim();
            }

            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static BackupEntry[] BuildEntries(string path, string backupId)
        {
            return new[]
            {
                BuildEntry(path, backupId),
                BuildEntry(path + ".meta", backupId),
            };
        }

        private static BackupEntry BuildEntry(string path, string backupId)
        {
            bool isDirectory = Directory.Exists(path);
            bool wasPresent = isDirectory || File.Exists(path);
            return new BackupEntry(path, BuildBackupPath(path, backupId), wasPresent, isDirectory);
        }

        private static void CreateBackup(BackupEntry entry, BackupMode mode, FileBackupTransactionOptions options)
        {
            if (PathExists(entry.BackupPath) || PathExists(entry.BackupPath + ".meta"))
            {
                options.WarningSink?.Invoke("stale bridge backup을 덮어씁니다: " + entry.BackupPath);
                DeleteBackupArtifacts(entry.BackupPath);
            }

            if (mode == BackupMode.Move)
            {
                MovePath(entry.OriginalPath, entry.BackupPath, entry.IsDirectory);
            }
            else if (entry.IsDirectory)
            {
                CopyDirectory(entry.OriginalPath, entry.BackupPath);
            }
            else
            {
                File.Copy(entry.OriginalPath, entry.BackupPath, overwrite: true);
            }

            TryMarkHidden(entry.BackupPath);
        }

        private static void RestoreEntries(BackupEntry[] entries, BackupMode mode)
        {
            for (int index = 0; index < entries.Length; index++)
            {
                BackupEntry entry = entries[index];
                if (PathExists(entry.OriginalPath))
                {
                    DeletePath(entry.OriginalPath);
                }

                if (!entry.WasPresent)
                {
                    continue;
                }

                if (mode == BackupMode.Move)
                {
                    MovePath(entry.BackupPath, entry.OriginalPath, entry.IsDirectory);
                }
                else if (entry.IsDirectory)
                {
                    CopyDirectory(entry.BackupPath, entry.OriginalPath);
                }
                else
                {
                    File.Copy(entry.BackupPath, entry.OriginalPath, overwrite: true);
                }
            }
        }

        private static void CleanupBackups(List<BackupEntry> backedUpEntries, FileBackupTransactionOptions options)
        {
            for (int index = 0; index < backedUpEntries.Count; index++)
            {
                BackupEntry entry = backedUpEntries[index];
                try
                {
                    DeleteBackupArtifacts(entry.BackupPath);
                }
                catch (Exception exception)
                {
                    options.WarningSink?.Invoke("bridge backup cleanup에 실패했습니다: " + entry.BackupPath + " (" + exception.Message + ")");
                }
            }
        }

        private static void DeleteBackupArtifacts(string backupPath)
        {
            if (PathExists(backupPath))
            {
                DeletePath(backupPath);
            }

            string generatedMetaPath = backupPath + ".meta";
            if (PathExists(generatedMetaPath))
            {
                DeletePath(generatedMetaPath);
            }
        }

        private static FileBackupTransactionException CreateBackupFailedException(
            string path,
            string commandName,
            BackupEntry[] entries,
            Exception exception)
        {
            string message = commandName + " 백업 생성에 실패했습니다: " + path;
            string details = BuildDetails(entries, exception);
            return new FileBackupTransactionException(ProtocolConstants.ErrorBackupFailed, message, details, exception);
        }

        private static FileBackupTransactionException CreateRestoreFailedException(
            string path,
            string commandName,
            List<BackupEntry> backedUpEntries,
            Exception actionException,
            Exception restoreException)
        {
            string backupLocations = BuildBackupLocationMessage(backedUpEntries);
            string message = commandName
                + " 실패 후 백업 복원에 실패했습니다. 수동 복구가 필요합니다. 백업 위치: "
                + backupLocations
                + ". 원인: "
                + restoreException.Message;
            string details = "original: " + actionException + Environment.NewLine + "restore: " + restoreException;
            return new FileBackupTransactionException(ProtocolConstants.ErrorBackupRestoreFailed, message, details, restoreException);
        }

        private static string BuildDetails(BackupEntry[] entries, Exception exception)
        {
            return "backups: " + BuildBackupLocationMessage(entries) + Environment.NewLine + exception;
        }

        private static string BuildBackupLocationMessage(IReadOnlyList<BackupEntry> entries)
        {
            if (entries.Count == 0)
            {
                return "(생성된 백업 없음)";
            }

            var locations = new List<string>(entries.Count);
            for (int index = 0; index < entries.Count; index++)
            {
                BackupEntry entry = entries[index];
                if (entry.WasPresent)
                {
                    locations.Add(entry.BackupPath);
                }
            }

            return locations.Count == 0 ? "(생성된 백업 없음)" : string.Join(", ", locations.ToArray());
        }

        private static bool PathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static void DeletePath(string path)
        {
            if (File.Exists(path))
            {
                TryClearReadonly(path);
                File.Delete(path);
                return;
            }

            if (Directory.Exists(path))
            {
                ClearReadonlyRecursive(path);
                Directory.Delete(path, recursive: true);
            }
        }

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);
            foreach (string directoryPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relativePath = directoryPath.Substring(sourcePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
            }

            foreach (string filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relativePath = filePath.Substring(sourcePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationFile = Path.Combine(destinationPath, relativePath);
                string? destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(filePath, destinationFile, overwrite: true);
            }
        }

        private static void MovePath(string sourcePath, string destinationPath, bool isDirectory)
        {
            if (isDirectory)
            {
                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        }

        private static void TryMarkHidden(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                File.SetAttributes(path, attributes | FileAttributes.Hidden);
            }
            catch
            {
                // A leading dot is the portable hidden marker; FileAttributes.Hidden is best effort.
            }
        }

        private static void ClearReadonlyRecursive(string directoryPath)
        {
            foreach (string filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                TryClearReadonly(filePath);
            }
        }

        private static void TryClearReadonly(string filePath)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
                // Cleanup and restore paths should still surface the original filesystem failure.
            }
        }

        private readonly struct BackupEntry
        {
            public BackupEntry(string originalPath, string backupPath, bool wasPresent, bool isDirectory)
            {
                OriginalPath = originalPath;
                BackupPath = backupPath;
                WasPresent = wasPresent;
                IsDirectory = isDirectory;
            }

            public string OriginalPath { get; }
            public string BackupPath { get; }
            public bool WasPresent { get; }
            public bool IsDirectory { get; }
        }
    }
}
