#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityCliBridge.Bridge.Editor
{
    public enum CliInstallStatus
    {
        NotInstalled,
        UpToDate,
        UpdateRequired,
    }

    public static class CliInstallerState
    {
        private const string InstalledVersionEditorPrefsKey = "UnityCliBridge.CLI.InstalledVersion";
        private const string LatestReleaseVersionKey = "UnityCliBridge.CLI.LatestReleaseVersion";
        private const string LatestReleaseCheckTimeKey = "UnityCliBridge.CLI.LatestReleaseCheckTime";
        private const string LatestReleaseFailureCheckTimeKey = "UnityCliBridge.CLI.LatestReleaseFailureCheckTime";
        private const string PackageJsonFileName = "package.json";
        private const string RepositoryUrl = "https://github.com/yhc509/unity-cli-bridge";
        private const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/yhc509/unity-cli-bridge/releases/latest";
        private const string GitHubApiUserAgent = "unity-cli-bridge";
        private const string ReleaseDownloadUrlPattern = RepositoryUrl + "/releases/download/v{0}/unity-cli-{1}.{2}";
        private const string ReleasePageUrlPattern = RepositoryUrl + "/releases/tag/v{0}";
        private const string SymlinkExecutablePath = "/bin/ln";
        private const string MacPlatformAssetName = "osx-arm64";
        private const string WindowsPlatformAssetName = "win-x64";
        private const string MacArchiveExtension = "tar.gz";
        private const string WindowsArchiveExtension = "zip";
        private const string MacPlatformDisplayName = "macOS arm64";
        private const string WindowsPlatformDisplayName = "Windows x64";
        private const int CacheExpirationMinutes = 60;
        private const int FailureRetryDelayMinutes = 2;
        private const int LatestReleaseRequestTimeoutSeconds = 15;
        private static LatestReleaseFetchOperation? _activeLatestReleaseFetch;

        /// <summary>The stable directory users put on PATH. Holds a symlink (macOS) or a copy (Windows).</summary>
        public static string GetInstallDirectory()
        {
            return CliInstallLayout.GetPathTargetDirectory();
        }

        public static string GetExecutablePath()
        {
            return CliInstallLayout.GetPathTargetExecutablePath();
        }

        public static string GetVersionInstallDirectory(string version)
        {
            return CliInstallLayout.GetVersionDirectory(version);
        }

        public static string GetProtocolVersion()
        {
            return ProtocolConstants.ProtocolVersion;
        }

        public static bool IsVersionInstalled(string version)
        {
            return !string.IsNullOrWhiteSpace(version)
                && File.Exists(CliInstallLayout.GetVersionExecutablePath(version));
        }

        public static IReadOnlyList<InstalledCliVersion> ListInstalledVersions()
        {
            List<InstalledCliVersion> installedVersions = CliInstallLayout.ListInstalled();
            installedVersions.Sort((left, right) => CliInstallLayout.CompareVersions(right.Version, left.Version));
            return installedVersions;
        }

        /// <summary>Version the PATH target currently resolves to, read from its meta.json marker.</summary>
        public static string? GetPathTargetVersion()
        {
            if (!File.Exists(CliInstallLayout.GetPathTargetExecutablePath()))
            {
                return null;
            }

            CliVersionMeta? meta = CliInstallLayout.TryReadMeta(CliInstallLayout.GetPathTargetMetaPath());
            return meta == null || string.IsNullOrWhiteSpace(meta.cliVersion)
                ? null
                : meta.cliVersion.Trim();
        }

        /// <summary>
        /// True when the PATH target is a real binary this Manager did not place there: either a
        /// pre-0.4.1 flat install, or the result of an older Manager overwriting the dispatcher.
        /// The meta.json marker is the discriminator, so this works on Windows (copy) as well as
        /// macOS/Linux (symlink).
        /// </summary>
        public static bool IsLegacyFlatInstallPresent()
        {
            return File.Exists(CliInstallLayout.GetPathTargetExecutablePath())
                && !File.Exists(CliInstallLayout.GetPathTargetMetaPath());
        }

        public static bool IsPathTargetCurrent()
        {
            string? pathTargetVersion = GetPathTargetVersion();
            if (string.IsNullOrWhiteSpace(pathTargetVersion))
            {
                return false;
            }

            InstalledCliVersion? newest = CliInstallLayout.FindNewest(CliInstallLayout.ListInstalled());

            // Ordinal, not CompareVersions: both sides were normalized when we wrote them, and
            // CompareVersions also returns 0 for versions it cannot parse.
            return newest != null
                && string.Equals(pathTargetVersion, newest.Version, StringComparison.Ordinal);
        }

        public static string? GetInstalledVersion()
        {
            string installedVersion = EditorPrefs.GetString(InstalledVersionEditorPrefsKey, string.Empty).Trim();
            return installedVersion.Length == 0 ? null : installedVersion;
        }

        public static string GetPackageVersion()
        {
            PackageManagerInfo? packageInfo = PackageManagerInfo.FindForAssembly(typeof(CliInstallerState).Assembly);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.version))
            {
                return packageInfo.version.Trim();
            }

            string packageJsonPath = Path.Combine(GetPackageDirectory(), PackageJsonFileName);
            if (!File.Exists(packageJsonPath))
            {
                throw new FileNotFoundException("Could not find package.json for the Unity package.", packageJsonPath);
            }

            JObject packageJson = JObject.Parse(File.ReadAllText(packageJsonPath));
            string? packageVersion = packageJson["version"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                throw new InvalidOperationException("package.json does not contain a version field.");
            }

            return packageVersion;
        }

        public static string? GetCachedLatestReleaseVersion()
        {
            string latestReleaseVersion = EditorPrefs.GetString(LatestReleaseVersionKey, string.Empty).Trim();
            return latestReleaseVersion.Length == 0 ? null : latestReleaseVersion;
        }

        public static bool IsLatestReleaseCacheExpired()
        {
            string cachedCheckTime = EditorPrefs.GetString(LatestReleaseCheckTimeKey, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cachedCheckTime))
            {
                return IsLatestReleaseFailureBackoffExpired();
            }

            if (!DateTimeOffset.TryParseExact(
                    cachedCheckTime,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset lastCheckTimeUtc))
            {
                return IsLatestReleaseFailureBackoffExpired();
            }

            int cacheExpirationMinutes = string.IsNullOrWhiteSpace(GetCachedLatestReleaseVersion())
                ? FailureRetryDelayMinutes
                : CacheExpirationMinutes;
            if (lastCheckTimeUtc.AddMinutes(cacheExpirationMinutes) > DateTimeOffset.UtcNow)
            {
                return false;
            }

            return IsLatestReleaseFailureBackoffExpired();
        }

        public static void FetchLatestReleaseVersion(Action<LatestReleaseFetchResult> onComplete)
        {
            if (onComplete == null)
            {
                throw new ArgumentNullException(nameof(onComplete));
            }

            if (_activeLatestReleaseFetch != null)
            {
                _activeLatestReleaseFetch.AddOnComplete(onComplete);
                return;
            }

            UnityWebRequest request = UnityWebRequest.Get(GitHubApiLatestReleaseUrl);
            request.SetRequestHeader("User-Agent", GitHubApiUserAgent);
            request.timeout = LatestReleaseRequestTimeoutSeconds;

            _activeLatestReleaseFetch = new LatestReleaseFetchOperation(request, onComplete);

            request.SendWebRequest();
            EditorApplication.update += PollLatestReleaseFetch;
        }

        public static string GetDownloadUrl(string releaseVersion)
        {
            if (string.IsNullOrWhiteSpace(releaseVersion))
            {
                throw new ArgumentException("Release version is required.", nameof(releaseVersion));
            }

            string normalizedReleaseVersion = NormalizeVersion(releaseVersion);
            GetPlatformAssetInfo(out string platformAssetName, out string archiveExtension);
            return string.Format(
                ReleaseDownloadUrlPattern,
                normalizedReleaseVersion,
                platformAssetName,
                archiveExtension);
        }

        public static string GetRepositoryUrl()
        {
            return RepositoryUrl;
        }

        public static string GetReleasePageUrl(string? releaseVersion)
        {
            return string.IsNullOrWhiteSpace(releaseVersion)
                ? string.Empty
                : string.Format(ReleasePageUrlPattern, NormalizeVersion(releaseVersion));
        }

        public static string GetPlatformDisplayName()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    return MacPlatformDisplayName;
                case RuntimePlatform.WindowsEditor:
                    return WindowsPlatformDisplayName;
                default:
                    throw new PlatformNotSupportedException("CLI Installer only supports macOS arm64 and Windows x64 editors.");
            }
        }

        public static CliInstallStatus GetStatus()
        {
            return GetStatus(GetPackageVersion());
        }

        /// <summary>
        /// Status is keyed on this package's own version, not on the newest release: a project on
        /// package v0.3.5 needs CLI v0.3.5 (protocol 4), and installing anything else would leave it
        /// unable to talk to its own bridge.
        /// </summary>
        public static CliInstallStatus GetStatus(string? packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageVersion) || !IsVersionInstalled(packageVersion!))
            {
                return CliInstallStatus.NotInstalled;
            }

            return IsPathTargetCurrent()
                ? CliInstallStatus.UpToDate
                : CliInstallStatus.UpdateRequired;
        }

        public static void SetInstalledVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("Installed version value is required.", nameof(version));
            }

            EditorPrefs.SetString(InstalledVersionEditorPrefsKey, version.Trim());
        }

        /// <summary>
        /// Runs after a versioned install lands on disk: records its protocol, archives any legacy
        /// flat install that would otherwise be destroyed, and points the PATH target at the newest
        /// installed version.
        /// </summary>
        public static void FinalizeInstall(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("Installed version value is required.", nameof(version));
            }

            string normalizedVersion = CliInstallLayout.NormalizeVersion(version);
            if (!IsVersionInstalled(normalizedVersion))
            {
                throw new FileNotFoundException(
                    "CLI executable not found for version " + normalizedVersion + ".",
                    CliInstallLayout.GetVersionExecutablePath(normalizedVersion));
            }

            CliInstallLayout.WriteMeta(
                CliInstallLayout.GetVersionMetaPath(normalizedVersion),
                normalizedVersion,
                ProtocolConstants.ProtocolVersion);

            MigrateLegacyFlatInstall();
            SetInstalledVersion(normalizedVersion);
            SetPathTargetToNewestInstalledVersion();
        }

        /// <summary>
        /// Clears the PATH target of any version-less binary before it gets replaced, so that
        /// repointing never destroys a CLI the user may still need.
        ///
        /// The version comes from the EditorPrefs record the Manager wrote when it installed that
        /// binary; shipped CLIs cannot report their own version. When we can identify it, it is
        /// archived into versions/&lt;version&gt;/ and becomes a hand-off candidate. When we cannot —
        /// a manual download (README "Option B") leaves no record, and a CLI newer than this package
        /// could have bumped the protocol — we refuse to guess a protocol for it, but we still must
        /// not delete it: it may be the only binary the user has for an older project. Those go to
        /// orphaned/ instead, and the Manager surfaces them.
        /// </summary>
        /// <returns>The archived version, or null when nothing was archived.</returns>
        public static string? MigrateLegacyFlatInstall()
        {
            if (!IsLegacyFlatInstallPresent())
            {
                return null;
            }

            string legacyExecutablePath = CliInstallLayout.GetPathTargetExecutablePath();
            if (IsSymbolicLink(legacyExecutablePath))
            {
                // A managed symlink whose marker went missing. Deleting a link destroys nothing.
                return null;
            }

            string? legacyVersion = GetInstalledVersion();
            string? legacyProtocolVersion = null;
            string normalizedLegacyVersion = string.Empty;
            if (!string.IsNullOrWhiteSpace(legacyVersion))
            {
                normalizedLegacyVersion = CliInstallLayout.NormalizeVersion(legacyVersion!);
                legacyProtocolVersion = CliInstallLayout.InferProtocolVersionForCliVersion(
                    normalizedLegacyVersion,
                    GetPackageVersion());
            }

            if (string.IsNullOrWhiteSpace(legacyProtocolVersion))
            {
                QuarantineLegacyFlatInstall(legacyExecutablePath);
                return null;
            }

            if (IsVersionInstalled(normalizedLegacyVersion))
            {
                // Already archived under this version, so the flat copy is redundant, not precious.
                return null;
            }

            string destinationDirectory = CliInstallLayout.GetVersionDirectory(normalizedLegacyVersion);
            Directory.CreateDirectory(destinationDirectory);
            File.Move(legacyExecutablePath, CliInstallLayout.GetVersionExecutablePath(normalizedLegacyVersion));
            CliInstallLayout.WriteMeta(
                CliInstallLayout.GetVersionMetaPath(normalizedLegacyVersion),
                normalizedLegacyVersion,
                legacyProtocolVersion!);

            return normalizedLegacyVersion;
        }

        /// <summary>Directories under orphaned/, newest first. Each holds one unidentifiable CLI binary.</summary>
        public static IReadOnlyList<string> ListOrphanedInstalls()
        {
            string orphanedDirectory = CliInstallLayout.GetOrphanedDirectory();
            if (!Directory.Exists(orphanedDirectory))
            {
                return Array.Empty<string>();
            }

            List<string> orphanedInstalls = new List<string>(Directory.GetDirectories(orphanedDirectory));
            orphanedInstalls.Sort(StringComparer.Ordinal);
            orphanedInstalls.Reverse();
            return orphanedInstalls;
        }

        /// <returns>True when the directory existed and was removed.</returns>
        public static bool RemoveOrphanedInstall(string orphanedDirectory)
        {
            if (string.IsNullOrWhiteSpace(orphanedDirectory))
            {
                throw new ArgumentException("Orphaned install directory is required.", nameof(orphanedDirectory));
            }

            string orphanedRoot = Path.GetFullPath(CliInstallLayout.GetOrphanedDirectory());
            string fullPath = Path.GetFullPath(orphanedDirectory);
            if (!fullPath.StartsWith(orphanedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Refusing to remove a path outside the orphaned directory: " + orphanedDirectory,
                    nameof(orphanedDirectory));
            }

            if (!Directory.Exists(fullPath))
            {
                return false;
            }

            Directory.Delete(fullPath, true);
            return true;
        }

        private static void QuarantineLegacyFlatInstall(string legacyExecutablePath)
        {
            string quarantineDirectory = Path.Combine(
                CliInstallLayout.GetOrphanedDirectory(),
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(quarantineDirectory);

            string destinationPath = Path.Combine(quarantineDirectory, Path.GetFileName(legacyExecutablePath));
            File.Move(legacyExecutablePath, destinationPath);

            Debug.LogWarning(
                "Unity CLI Bridge: the CLI binary at " + legacyExecutablePath
                + " could not be identified (no recorded version), so it is not a hand-off candidate. "
                + "It was moved to " + destinationPath + " rather than deleted, because it may be the only "
                + "CLI you have for a project on an older package version. "
                + "Window > Unity CLI Manager lists it under Unidentified Binaries.");
        }

        /// <summary>Points ~/.unity-cli-bridge/unity-cli/ at the newest installed version.</summary>
        public static void SetPathTargetToNewestInstalledVersion()
        {
            InstalledCliVersion? newest = CliInstallLayout.FindNewest(CliInstallLayout.ListInstalled());
            if (newest == null)
            {
                ClearPathTarget();
                return;
            }

            string pathTargetDirectory = CliInstallLayout.GetPathTargetDirectory();
            Directory.CreateDirectory(pathTargetDirectory);

            string pathTargetExecutablePath = CliInstallLayout.GetPathTargetExecutablePath();
            DeleteFileIfExists(pathTargetExecutablePath);

            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    // File.CreateSymbolicLink is .NET 6+; the Editor's runtime does not have it.
                    CliDownloader.RunProcess(
                        SymlinkExecutablePath,
                        "-sfn " + QuoteArgument(newest.ExecutablePath) + " " + QuoteArgument(pathTargetExecutablePath),
                        "symlink PATH target");
                    break;
                case RuntimePlatform.WindowsEditor:
                    // Symlinks on Windows need developer mode or elevation, so copy instead.
                    File.Copy(newest.ExecutablePath, pathTargetExecutablePath, true);
                    break;
                default:
                    throw new PlatformNotSupportedException("CLI Installer only supports macOS arm64 and Windows x64 editors.");
            }

            CliInstallLayout.WriteMeta(
                CliInstallLayout.GetPathTargetMetaPath(),
                newest.Version,
                newest.ProtocolVersion);
        }

        /// <returns>True when the version existed and was removed.</returns>
        public static bool RemoveVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("Version is required.", nameof(version));
            }

            string versionDirectory = CliInstallLayout.GetVersionDirectory(version);
            if (!Directory.Exists(versionDirectory))
            {
                return false;
            }

            Directory.Delete(versionDirectory, true);
            SetPathTargetToNewestInstalledVersion();
            return true;
        }

        private static void ClearPathTarget()
        {
            DeleteFileIfExists(CliInstallLayout.GetPathTargetExecutablePath());
            DeleteFileIfExists(CliInstallLayout.GetPathTargetMetaPath());
        }

        private static void DeleteFileIfExists(string filePath)
        {
            // File.Delete removes a symlink itself, not what it points at.
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private static bool IsSymbolicLink(string filePath)
        {
            try
            {
                return (File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void PollLatestReleaseFetch()
        {
            if (_activeLatestReleaseFetch == null)
            {
                EditorApplication.update -= PollLatestReleaseFetch;
                return;
            }

            LatestReleaseFetchOperation operation = _activeLatestReleaseFetch;

            if (!operation.Request.isDone)
            {
                return;
            }

            EditorApplication.update -= PollLatestReleaseFetch;
            _activeLatestReleaseFetch = null;

            LatestReleaseFetchResult result;
            try
            {
                if (operation.Request.result == UnityWebRequest.Result.Success
                    && operation.Request.responseCode == 200)
                {
                    string? fetchedLatestReleaseVersion = ParseLatestReleaseVersion(operation.Request.downloadHandler.text);
                    if (!string.IsNullOrWhiteSpace(fetchedLatestReleaseVersion))
                    {
                        SetLatestReleaseCache(fetchedLatestReleaseVersion);
                        result = LatestReleaseFetchResult.Success(fetchedLatestReleaseVersion);
                    }
                    else
                    {
                        SetLatestReleaseCache(null);
                        result = LatestReleaseFetchResult.Success(null);
                    }
                }
                else if (operation.Request.responseCode == 404)
                {
                    SetLatestReleaseCache(null);
                    result = LatestReleaseFetchResult.Success(null);
                }
                else
                {
                    SetLatestReleaseFailureBackoff();
                    result = LatestReleaseFetchResult.Failure(GetCachedLatestReleaseVersion());
                }
            }
            catch
            {
                // Parse failure: keep the last known good version from cache.
                SetLatestReleaseFailureBackoff();
                result = LatestReleaseFetchResult.Failure(GetCachedLatestReleaseVersion());
            }

            try
            {
                operation.Complete(result);
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static string GetPackageDirectory()
        {
            PackageManagerInfo? packageInfo = PackageManagerInfo.FindForAssembly(typeof(CliInstallerState).Assembly);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                throw new InvalidOperationException("Could not resolve the Unity package path.");
            }

            return packageInfo.resolvedPath;
        }

        internal static int CompareVersions(string leftVersion, string rightVersion)
        {
            return CliInstallLayout.CompareVersions(leftVersion, rightVersion);
        }

        private static string? ParseLatestReleaseVersion(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            JObject responseJson = JObject.Parse(responseText);
            bool isDraft = responseJson["draft"]?.Value<bool>() ?? false;
            bool isPrerelease = responseJson["prerelease"]?.Value<bool>() ?? false;
            if (isDraft || isPrerelease)
            {
                return null;
            }

            string? tagName = responseJson["tag_name"]?.Value<string>()?.Trim();
            return string.IsNullOrWhiteSpace(tagName)
                ? null
                : NormalizeVersion(tagName);
        }

        private static string NormalizeVersion(string version)
        {
            return version.Trim().TrimStart('v', 'V');
        }

        private static void SetLatestReleaseCache(string? latestReleaseVersion)
        {
            EditorPrefs.SetString(
                LatestReleaseVersionKey,
                string.IsNullOrWhiteSpace(latestReleaseVersion)
                    ? string.Empty
                    : latestReleaseVersion.Trim());
            EditorPrefs.SetString(
                LatestReleaseCheckTimeKey,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            EditorPrefs.DeleteKey(LatestReleaseFailureCheckTimeKey);
        }

        private static bool IsLatestReleaseFailureBackoffExpired()
        {
            string cachedFailureCheckTime = EditorPrefs.GetString(LatestReleaseFailureCheckTimeKey, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cachedFailureCheckTime))
            {
                return true;
            }

            if (!DateTimeOffset.TryParseExact(
                    cachedFailureCheckTime,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset lastFailureCheckTimeUtc))
            {
                return true;
            }

            return lastFailureCheckTimeUtc.AddMinutes(FailureRetryDelayMinutes) <= DateTimeOffset.UtcNow;
        }

        private static void SetLatestReleaseFailureBackoff()
        {
            EditorPrefs.SetString(
                LatestReleaseFailureCheckTimeKey,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }

        private static void GetPlatformAssetInfo(out string platformAssetName, out string archiveExtension)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    platformAssetName = MacPlatformAssetName;
                    archiveExtension = MacArchiveExtension;
                    return;
                case RuntimePlatform.WindowsEditor:
                    platformAssetName = WindowsPlatformAssetName;
                    archiveExtension = WindowsArchiveExtension;
                    return;
                default:
                    throw new PlatformNotSupportedException("CLI Installer only supports macOS arm64 and Windows x64 editors.");
            }
        }

        private sealed class LatestReleaseFetchOperation : IDisposable
        {
            private Action<LatestReleaseFetchResult> _onComplete;

            public LatestReleaseFetchOperation(UnityWebRequest request, Action<LatestReleaseFetchResult> onComplete)
            {
                Request = request ?? throw new ArgumentNullException(nameof(request));
                _onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            }

            public UnityWebRequest Request { get; }

            public void AddOnComplete(Action<LatestReleaseFetchResult> onComplete)
            {
                if (onComplete == null)
                {
                    throw new ArgumentNullException(nameof(onComplete));
                }

                _onComplete += onComplete;
            }

            public void Complete(LatestReleaseFetchResult result)
            {
                Delegate[] subscribers = _onComplete.GetInvocationList();
                for (int i = 0; i < subscribers.Length; i++)
                {
                    Action<LatestReleaseFetchResult> subscriber = (Action<LatestReleaseFetchResult>)subscribers[i];
                    try
                    {
                        subscriber(result);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }

            public void Dispose()
            {
                Request.Dispose();
            }
        }

        public readonly struct LatestReleaseFetchResult
        {
            private LatestReleaseFetchResult(bool succeeded, string? latestReleaseVersion)
            {
                Succeeded = succeeded;
                LatestReleaseVersion = latestReleaseVersion;
            }

            public bool Succeeded { get; }

            public string? LatestReleaseVersion { get; }

            public static LatestReleaseFetchResult Success(string? latestReleaseVersion)
            {
                return new LatestReleaseFetchResult(true, latestReleaseVersion);
            }

            public static LatestReleaseFetchResult Failure(string? latestReleaseVersion)
            {
                return new LatestReleaseFetchResult(false, latestReleaseVersion);
            }
        }
    }
}
