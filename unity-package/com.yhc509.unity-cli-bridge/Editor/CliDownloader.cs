#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityCliBridge.Bridge.Editor
{
    public static class CliDownloader
    {
        private const string TarExecutablePath = "/usr/bin/tar";
        private const string ChmodExecutablePath = "/bin/chmod";
        private const int DownloadRequestTimeoutSeconds = 60;
        private static DownloadInstallOperation? _activeOperation;

        public static void DownloadAndInstallAsync(
            string url,
            string releaseVersion,
            string installDir,
            Action<float> onProgress,
            Action<string> onError,
            Action onComplete)
        {
            if (_activeOperation != null)
            {
                throw new InvalidOperationException("A CLI download is already in progress.");
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Download URL is required.", nameof(url));
            }

            // This method is public and takes an arbitrary URL; pin it to the release location
            // before a single byte is fetched.
            CliInstallGuard.EnsureTrustedDownloadUrl(url);

            if (string.IsNullOrWhiteSpace(releaseVersion))
            {
                throw new ArgumentException("Release version is required.", nameof(releaseVersion));
            }

            if (string.IsNullOrWhiteSpace(installDir))
            {
                throw new ArgumentException("Install directory path is required.", nameof(installDir));
            }

            if (onProgress == null)
            {
                throw new ArgumentNullException(nameof(onProgress));
            }

            if (onError == null)
            {
                throw new ArgumentNullException(nameof(onError));
            }

            if (onComplete == null)
            {
                throw new ArgumentNullException(nameof(onComplete));
            }

            string archivePath = CreateTemporaryArchivePath(url);
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = new DownloadHandlerFile(archivePath);
            request.timeout = DownloadRequestTimeoutSeconds;

            _activeOperation = new DownloadInstallOperation(
                request,
                archivePath,
                releaseVersion.Trim(),
                installDir,
                onProgress,
                onError,
                onComplete);

            request.SendWebRequest();
            EditorApplication.update += PollDownload;
        }

        private static void PollDownload()
        {
            DownloadInstallOperation operation = _activeOperation
                ?? throw new InvalidOperationException("No active CLI download operation.");

            operation.ReportProgress();
            if (!operation.Request.isDone)
            {
                return;
            }

            EditorApplication.update -= PollDownload;
            _activeOperation = null;

            try
            {
                if (operation.Request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException("CLI download failed: " + operation.Request.error);
                }

                operation.OnProgress(0.95f);
                InstallArchive(operation.ArchivePath, operation.InstallDirectory);
                // Records the protocol, archives any legacy flat install, and repoints the PATH target.
                CliInstallerState.FinalizeInstall(operation.ReleaseVersion);
                operation.OnProgress(1f);
                operation.OnComplete();
            }
            catch (Exception exception)
            {
                operation.OnError(exception.Message);
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static void InstallArchive(string archivePath, string installDir)
        {
            string stagedInstallDirectory = CreateTemporaryDirectory();

            try
            {
                ExtractArchive(archivePath, stagedInstallDirectory);
                // Before anything is read out of staging: EnsureExecutableExists and the copy
                // both dereference symlinks, so the archive must be proven symlink-free first.
                CliInstallGuard.EnsureNoSymlinks(stagedInstallDirectory);
                EnsureExecutableExists(stagedInstallDirectory);
                ReplaceInstallDirectory(stagedInstallDirectory, installDir);

                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    SetExecutablePermission(Path.Combine(installDir, CliInstallLayout.GetExecutableFileName()));
                }

                EnsureExecutableExists(installDir);
            }
            finally
            {
                CleanupDirectory(stagedInstallDirectory);
            }
        }

        private static void ExtractArchive(string archivePath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    RunProcess(
                        TarExecutablePath,
                        "-xzf " + QuoteArgument(archivePath) + " -C " + QuoteArgument(destinationDirectory),
                        "tar extraction");
                    return;
                case RuntimePlatform.WindowsEditor:
                    ZipFile.ExtractToDirectory(archivePath, destinationDirectory);
                    return;
                default:
                    throw new PlatformNotSupportedException("CLI Installer only supports macOS arm64 and Windows x64 editors.");
            }
        }

        private static void ReplaceInstallDirectory(string sourceDirectory, string installDir)
        {
            string? parentDirectory = Path.GetDirectoryName(installDir);
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                throw new InvalidOperationException("Could not resolve parent directory for install path: " + installDir);
            }

            Directory.CreateDirectory(parentDirectory);
            string stagedTargetDirectory = Path.Combine(
                parentDirectory,
                ".unity-cli-install-" + Guid.NewGuid().ToString("N"));
            string backupDirectory = Path.Combine(
                parentDirectory,
                ".unity-cli-backup-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(stagedTargetDirectory);
                CopyDirectoryContents(sourceDirectory, stagedTargetDirectory);

                if (Directory.Exists(installDir))
                {
                    Directory.Move(installDir, backupDirectory);
                }

                Directory.Move(stagedTargetDirectory, installDir);
                stagedTargetDirectory = string.Empty;

                CleanupDirectory(backupDirectory);
                backupDirectory = string.Empty;
            }
            catch
            {
                if (Directory.Exists(backupDirectory) && !Directory.Exists(installDir))
                {
                    Directory.Move(backupDirectory, installDir);
                    backupDirectory = string.Empty;
                }

                throw;
            }
            finally
            {
                CleanupDirectory(stagedTargetDirectory);
                CleanupDirectory(backupDirectory);
            }
        }

        private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
        {
            foreach (string sourceSubdirectory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = sourceSubdirectory.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
            }

            foreach (string sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = sourceFilePath.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationFilePath = Path.Combine(destinationDirectory, relativePath);
                string? destinationParentDirectory = Path.GetDirectoryName(destinationFilePath);
                if (string.IsNullOrWhiteSpace(destinationParentDirectory))
                {
                    throw new InvalidOperationException("Could not resolve parent directory for destination file: " + destinationFilePath);
                }

                Directory.CreateDirectory(destinationParentDirectory);
                File.Copy(sourceFilePath, destinationFilePath, true);
            }
        }

        private static void EnsureExecutableExists(string installDirectory)
        {
            string executablePath = Path.Combine(installDirectory, CliInstallLayout.GetExecutableFileName());
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("CLI executable not found after extraction.", executablePath);
            }
        }

        private static void SetExecutablePermission(string executablePath)
        {
            RunProcess(
                ChmodExecutablePath,
                "+x " + QuoteArgument(executablePath),
                "set executable permission");
        }

        internal static void RunProcess(string fileName, string arguments, string stepName)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };

                var stderrBuilder = new System.Text.StringBuilder();
                object stderrLock = new object();

                process.OutputDataReceived += (sender, e) =>
                {
                    // stdout is intentionally discarded; async drain prevents pipe-buffer deadlocks.
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    lock (stderrLock)
                    {
                        stderrBuilder.AppendLine(e.Data);
                    }
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException(stepName + " process failed to start.");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                string standardError;
                lock (stderrLock)
                {
                    standardError = stderrBuilder.ToString().Trim();
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(stepName + " failed: " + standardError);
                }
            }
        }

        private static string CreateTemporaryArchivePath(string url)
        {
            string fileExtension = Path.GetExtension(url);
            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                throw new InvalidOperationException("Could not determine archive extension from download URL: " + url);
            }

            return Path.Combine(Path.GetTempPath(), "unity-cli-bridge-" + Guid.NewGuid().ToString("N") + fileExtension);
        }

        private static string CreateTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "unity-cli-bridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private static void CleanupDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            Directory.Delete(directoryPath, true);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class DownloadInstallOperation : IDisposable
        {
            public DownloadInstallOperation(
                UnityWebRequest request,
                string archivePath,
                string releaseVersion,
                string installDirectory,
                Action<float> onProgress,
                Action<string> onError,
                Action onComplete)
            {
                Request = request;
                ArchivePath = archivePath;
                ReleaseVersion = releaseVersion;
                InstallDirectory = installDirectory;
                OnProgress = onProgress;
                OnError = onError;
                OnComplete = onComplete;
            }

            public UnityWebRequest Request { get; }

            public string ArchivePath { get; }

            public string ReleaseVersion { get; }

            public string InstallDirectory { get; }

            public Action<float> OnProgress { get; }

            public Action<string> OnError { get; }

            public Action OnComplete { get; }

            public void Dispose()
            {
                Request.Dispose();

                if (File.Exists(ArchivePath))
                {
                    File.Delete(ArchivePath);
                }
            }

            public void ReportProgress()
            {
                float progress = Request.isDone ? 1f : Mathf.Clamp01(Request.downloadProgress);
                OnProgress(progress);
            }
        }
    }
}
