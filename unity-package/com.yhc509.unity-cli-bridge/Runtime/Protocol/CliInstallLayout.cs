#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace UnityCli.Protocol
{
    /// <summary>
    /// Metadata written next to each versioned CLI install so that a running CLI can tell which
    /// wire protocol every installed binary speaks without executing it.
    /// </summary>
    [Serializable]
    public sealed class CliVersionMeta
    {
        public string cliVersion = string.Empty;
        public string protocolVersion = string.Empty;
    }

    /// <summary>What must happen to the PATH-target entry before it can be replaced.</summary>
    public enum PathTargetReleaseAction
    {
        /// <summary>Nothing is there.</summary>
        Nothing,

        /// <summary>A symlink. It owns no bytes, so it can always be unlinked.</summary>
        Unlink,

        /// <summary>A regular file whose bytes provably also live under versions/.</summary>
        Delete,

        /// <summary>A regular file we cannot prove is redundant. Never delete it; move it aside.</summary>
        Quarantine,
    }

    /// <summary>
    /// A CLI binary discovered under the versions directory, together with the protocol it speaks.
    /// Not a wire model: this never leaves the local machine.
    /// </summary>
    public sealed class InstalledCliVersion
    {
        public InstalledCliVersion(string version, string protocolVersion, string directory, string executablePath)
        {
            Version = version;
            ProtocolVersion = protocolVersion;
            Directory = directory;
            ExecutablePath = executablePath;
        }

        public string Version { get; }

        public string ProtocolVersion { get; }

        public string Directory { get; }

        public string ExecutablePath { get; }
    }

    /// <summary>
    /// Owns the on-disk layout of the machine-wide CLI install:
    /// <code>
    /// ~/.unity-cli-bridge/
    ///   unity-cli/                  PATH target (symlink on macOS/Linux, copy on Windows) + meta.json marker
    ///   versions/&lt;version&gt;/    versioned install + meta.json
    /// </code>
    /// Shared by the CLI (which scans it to dispatch on PROTOCOL_MISMATCH) and by the Unity
    /// Editor's CLI Manager (which populates it).
    /// </summary>
    public static class CliInstallLayout
    {
        /// <summary>Set on the child process before re-exec so the dispatched CLI never dispatches again.</summary>
        public const string DispatchGuardEnvironmentVariable = "UNITY_CLI_DISPATCHED";

        /// <summary>Test/override hook for the install root. Mirrors UNITY_CLI_REGISTRY_PATH.</summary>
        public const string InstallRootEnvironmentVariable = "UNITY_CLI_INSTALL_ROOT";

        public const string MetaFileName = "meta.json";

        private const string InstallRootDirectoryName = ".unity-cli-bridge";
        private const string PathTargetDirectoryName = "unity-cli";
        private const string VersionsDirectoryName = "versions";
        private const string OrphanedDirectoryName = "orphaned";
        private const string UnixExecutableName = "unity-cli";
        private const string WindowsExecutableName = "unity-cli.exe";
        private const string Protocol4FirstCliVersion = "0.1.13";
        private const string Protocol5FirstCliVersion = "0.4.0";
        private const string Protocol4 = "4";
        private const string Protocol5 = "5";
        private const int FileCompareBufferSize = 64 * 1024;

        public static string GetInstallRoot()
        {
            string overrideRoot = Environment.GetEnvironmentVariable(InstallRootEnvironmentVariable) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return Path.GetFullPath(overrideRoot.Trim());
            }

            string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                throw new InvalidOperationException("Failed to resolve user home directory.");
            }

            return Path.Combine(homeDirectory, InstallRootDirectoryName);
        }

        /// <summary>The stable directory users put on PATH. Never renamed.</summary>
        public static string GetPathTargetDirectory()
        {
            return Path.Combine(GetInstallRoot(), PathTargetDirectoryName);
        }

        public static string GetPathTargetExecutablePath()
        {
            return Path.Combine(GetPathTargetDirectory(), GetExecutableFileName());
        }

        public static string GetPathTargetMetaPath()
        {
            return Path.Combine(GetPathTargetDirectory(), MetaFileName);
        }

        public static string GetVersionsDirectory()
        {
            return Path.Combine(GetInstallRoot(), VersionsDirectoryName);
        }

        /// <summary>
        /// Holding pen for a CLI binary we found at the PATH target but could not identify. It is not
        /// a dispatch candidate (we do not know its protocol) and it must never be deleted for us —
        /// it may be the only binary a user has for an older project.
        /// </summary>
        public static string GetOrphanedDirectory()
        {
            return Path.Combine(GetInstallRoot(), OrphanedDirectoryName);
        }

        public static string GetVersionDirectory(string version)
        {
            return Path.Combine(GetVersionsDirectory(), NormalizeVersion(version));
        }

        public static string GetVersionExecutablePath(string version)
        {
            return Path.Combine(GetVersionDirectory(version), GetExecutableFileName());
        }

        public static string GetVersionMetaPath(string version)
        {
            return Path.Combine(GetVersionDirectory(version), MetaFileName);
        }

        public static string GetExecutableFileName()
        {
            return Path.DirectorySeparatorChar == '\\' ? WindowsExecutableName : UnixExecutableName;
        }

        /// <summary>
        /// True when the file at the PATH target is byte-identical to the versioned binary its
        /// meta.json marker names — that is, removing it cannot destroy the last copy of those bytes.
        ///
        /// This is the proof required before deleting a REGULAR FILE at the PATH target (our copy on
        /// Windows, or anything a user hand-placed there). A symlink needs no such proof and is not
        /// routed through here: see <see cref="GetPathTargetReleaseAction"/>.
        /// </summary>
        public static bool IsPathTargetRedundant()
        {
            string pathTargetExecutablePath = GetPathTargetExecutablePath();
            if (!File.Exists(pathTargetExecutablePath))
            {
                return false;
            }

            CliVersionMeta? marker = TryReadMeta(GetPathTargetMetaPath());
            if (marker == null || string.IsNullOrWhiteSpace(marker.cliVersion))
            {
                return false;
            }

            string versionExecutablePath = GetVersionExecutablePath(marker.cliVersion.Trim());
            return File.Exists(versionExecutablePath)
                && FilesHaveSameContent(pathTargetExecutablePath, versionExecutablePath);
        }

        /// <summary>
        /// A regular file sits at the PATH target whose bytes exist nowhere else we manage. It may be
        /// the only CLI the user has for an older project, so it must never be deleted for them.
        ///
        /// A symlink is never "unmanaged": it owns no bytes, so removing it cannot cost the user
        /// anything, whatever it points at.
        /// </summary>
        public static bool IsUnmanagedPathTargetBinaryPresent()
        {
            return IsRegularFile(GetPathTargetExecutablePath()) && !IsPathTargetRedundant();
        }

        /// <summary>
        /// What has to happen to the PATH-target entry before it can be replaced. The whole
        /// never-destroy-the-user's-only-binary rule lives here so it can be tested without an Editor.
        /// </summary>
        public static PathTargetReleaseAction GetPathTargetReleaseAction()
        {
            string pathTargetExecutablePath = GetPathTargetExecutablePath();

            // Symlink first, and unconditionally. A link owns no bytes, so unlinking it can never
            // destroy a binary — not even a dangling one, and not one pointing outside versions/.
            // This is the macOS/Linux PATH target, and it covers the dangling case for free.
            if (IsSymbolicLink(pathTargetExecutablePath))
            {
                return PathTargetReleaseAction.Unlink;
            }

            if (!File.Exists(pathTargetExecutablePath))
            {
                return PathTargetReleaseAction.Nothing;
            }

            // A regular file. It only goes if its bytes provably live somewhere else.
            return IsPathTargetRedundant()
                ? PathTargetReleaseAction.Delete
                : PathTargetReleaseAction.Quarantine;
        }

        /// <summary>File.Exists is true for a dangling symlink, so link status must be asked separately.</summary>
        public static bool IsSymbolicLink(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                return (File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsRegularFile(string filePath)
        {
            return File.Exists(filePath) && !IsSymbolicLink(filePath);
        }

        public static bool FilesHaveSameContent(string leftFilePath, string rightFilePath)
        {
            if (string.IsNullOrWhiteSpace(leftFilePath) || string.IsNullOrWhiteSpace(rightFilePath))
            {
                return false;
            }

            try
            {
                if (!File.Exists(leftFilePath) || !File.Exists(rightFilePath))
                {
                    return false;
                }

                byte[] leftBuffer = new byte[FileCompareBufferSize];
                byte[] rightBuffer = new byte[FileCompareBufferSize];

                using (FileStream leftStream = File.OpenRead(leftFilePath))
                using (FileStream rightStream = File.OpenRead(rightFilePath))
                {
                    // Lengths come from the opened streams, never from FileInfo. FileInfo.Length is
                    // lstat-based: on a symlink it reports the length of the link's path string, not
                    // the target's size, so comparing FileInfo lengths would short-circuit false for
                    // every symlink and this proof could never succeed on macOS/Linux.
                    if (leftStream.Length != rightStream.Length)
                    {
                        return false;
                    }

                    while (true)
                    {
                        int leftRead = ReadBlock(leftStream, leftBuffer);
                        int rightRead = ReadBlock(rightStream, rightBuffer);
                        if (leftRead != rightRead)
                        {
                            return false;
                        }

                        if (leftRead == 0)
                        {
                            return true;
                        }

                        for (int i = 0; i < leftRead; i++)
                        {
                            if (leftBuffer[i] != rightBuffer[i])
                            {
                                return false;
                            }
                        }
                    }
                }
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

        /// <summary>
        /// Strict containment: the path must be a *proper descendant* of orphaned/. Trailing and
        /// redundant separators are normalized away first, because Path.GetFullPath preserves a
        /// trailing separator — without this, "orphaned/", "orphaned//" and "orphaned/./" would all
        /// pass as descendants of themselves, and a recursive delete would wipe every binary this
        /// directory exists to preserve.
        /// </summary>
        public static bool IsInsideOrphanedDirectory(string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            string orphanedRoot;
            string fullPath;
            try
            {
                orphanedRoot = TrimTrailingSeparators(Path.GetFullPath(GetOrphanedDirectory()));
                fullPath = TrimTrailingSeparators(Path.GetFullPath(candidatePath));
            }
            catch (Exception)
            {
                return false;
            }

            return fullPath.Length > orphanedRoot.Length
                && fullPath.StartsWith(orphanedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        private static string TrimTrailingSeparators(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static int ReadBlock(FileStream stream, byte[] buffer)
        {
            // Stream.ReadExactly is .NET 7+; the Editor's runtime does not have it.
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        public static bool IsDispatchGuardSet()
        {
            string guard = Environment.GetEnvironmentVariable(DispatchGuardEnvironmentVariable) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(guard);
        }

        /// <summary>
        /// Scans versions/*/ and returns every install that has both an executable and a readable
        /// meta.json. Directories with a missing or corrupt meta.json are skipped, not fatal:
        /// a partially written install must never take the whole CLI down.
        /// </summary>
        public static List<InstalledCliVersion> ListInstalled()
        {
            var installed = new List<InstalledCliVersion>();
            string versionsDirectory = GetVersionsDirectory();
            if (!Directory.Exists(versionsDirectory))
            {
                return installed;
            }

            string executableFileName = GetExecutableFileName();
            string[] versionDirectories;
            try
            {
                versionDirectories = Directory.GetDirectories(versionsDirectory);
            }
            catch (IOException)
            {
                return installed;
            }
            catch (UnauthorizedAccessException)
            {
                return installed;
            }

            for (int i = 0; i < versionDirectories.Length; i++)
            {
                string versionDirectory = versionDirectories[i];
                string directoryName = Path.GetFileName(versionDirectory);

                // The installer stages into `.unity-cli-install-<guid>/` and `.unity-cli-backup-<guid>/`
                // as siblings in here, and the backup holds a real meta.json. If cleanup ever fails,
                // one of those would otherwise pass every check below and become a phantom version the
                // PATH target could be pointed at. A version directory must be named for its version.
                if (!IsVersionDirectoryName(directoryName))
                {
                    continue;
                }

                string executablePath = Path.Combine(versionDirectory, executableFileName);
                if (!File.Exists(executablePath))
                {
                    continue;
                }

                CliVersionMeta? meta = TryReadMeta(Path.Combine(versionDirectory, MetaFileName));
                if (meta == null
                    || string.IsNullOrWhiteSpace(meta.cliVersion)
                    || string.IsNullOrWhiteSpace(meta.protocolVersion))
                {
                    continue;
                }

                string metaVersion = meta.cliVersion.Trim();
                if (!string.Equals(metaVersion, directoryName, StringComparison.Ordinal))
                {
                    continue;
                }

                installed.Add(new InstalledCliVersion(
                    metaVersion,
                    meta.protocolVersion.Trim(),
                    versionDirectory,
                    executablePath));
            }

            return installed;
        }

        /// <summary>
        /// A version directory is named for exactly the version it holds. Enforcing that is also what
        /// keeps <see cref="CompareVersions"/> honest downstream: it returns 0 for anything it cannot
        /// parse, so a single unparseable version in the candidate list would become an unbeatable
        /// maximum (nothing can compare greater than it) and poison every "newest wins" decision.
        /// </summary>
        public static bool IsVersionDirectoryName(string directoryName)
        {
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return false;
            }

            Version? core;
            string prerelease;
            return TryParseVersion(directoryName, out core, out prerelease)
                && core != null
                && string.Equals(NormalizeVersion(directoryName), directoryName, StringComparison.Ordinal);
        }

        /// <summary>Newest installed version that speaks <paramref name="protocolVersion"/>, or null.</summary>
        public static InstalledCliVersion? FindByProtocolVersion(
            IEnumerable<InstalledCliVersion> installedVersions,
            string protocolVersion)
        {
            if (installedVersions == null || string.IsNullOrWhiteSpace(protocolVersion))
            {
                return null;
            }

            string wanted = protocolVersion.Trim();
            InstalledCliVersion? best = null;
            foreach (InstalledCliVersion candidate in installedVersions)
            {
                if (candidate == null
                    || !string.Equals(candidate.ProtocolVersion, wanted, StringComparison.Ordinal))
                {
                    continue;
                }

                if (best == null || CompareVersions(candidate.Version, best.Version) > 0)
                {
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>Newest installed version regardless of protocol. This is what the PATH target points at.</summary>
        public static InstalledCliVersion? FindNewest(IEnumerable<InstalledCliVersion> installedVersions)
        {
            if (installedVersions == null)
            {
                return null;
            }

            InstalledCliVersion? best = null;
            foreach (InstalledCliVersion candidate in installedVersions)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (best == null || CompareVersions(candidate.Version, best.Version) > 0)
                {
                    best = candidate;
                }
            }

            return best;
        }

        public static CliVersionMeta? TryReadMeta(string metaFilePath)
        {
            if (string.IsNullOrWhiteSpace(metaFilePath) || !File.Exists(metaFilePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(metaFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return ProtocolJson.Deserialize<CliVersionMeta>(json);
            }
            catch (Exception)
            {
                // Corrupt or unreadable meta.json: treat the install as undiscoverable rather than fatal.
                return null;
            }
        }

        public static void WriteMeta(string metaFilePath, string cliVersion, string protocolVersion)
        {
            if (string.IsNullOrWhiteSpace(metaFilePath))
            {
                throw new ArgumentException("meta.json path is required.", "metaFilePath");
            }

            if (string.IsNullOrWhiteSpace(cliVersion))
            {
                throw new ArgumentException("CLI version is required.", "cliVersion");
            }

            if (string.IsNullOrWhiteSpace(protocolVersion))
            {
                throw new ArgumentException("Protocol version is required.", "protocolVersion");
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(metaFilePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var meta = new CliVersionMeta
            {
                cliVersion = NormalizeVersion(cliVersion),
                protocolVersion = protocolVersion.Trim(),
            };
            File.WriteAllText(metaFilePath, ProtocolJson.Serialize(meta));
        }

        /// <summary>
        /// Protocol version for a CLI release that predates versioned installs, derived from the
        /// released protocol history. Returns null when the version is older than the first release
        /// whose protocol is known, so callers can refuse to guess.
        ///
        /// This table only covers releases up to the one that ships it. Use the
        /// <c>maxKnownCliVersion</c> overload from anything that can meet a newer binary.
        /// **Extend this table whenever ProtocolConstants.ProtocolVersion changes.**
        /// </summary>
        public static string? InferProtocolVersionForCliVersion(string cliVersion)
        {
            Version? parsedCore;
            string prerelease;
            if (!TryParseVersion(cliVersion, out parsedCore, out prerelease) || parsedCore == null)
            {
                return null;
            }

            if (CompareVersions(cliVersion, Protocol5FirstCliVersion) >= 0)
            {
                return Protocol5;
            }

            return CompareVersions(cliVersion, Protocol4FirstCliVersion) >= 0
                ? Protocol4
                : null;
        }

        /// <summary>
        /// Same as <see cref="InferProtocolVersionForCliVersion(string)"/>, but refuses to answer for
        /// a CLI newer than <paramref name="maxKnownCliVersion"/>. A release past the caller's own
        /// version may have bumped the protocol, and the table cannot know that; guessing would route
        /// commands to a binary that cannot speak them.
        /// </summary>
        public static string? InferProtocolVersionForCliVersion(string cliVersion, string maxKnownCliVersion)
        {
            Version? knownCore;
            string knownPrerelease;
            if (!TryParseVersion(maxKnownCliVersion, out knownCore, out knownPrerelease) || knownCore == null)
            {
                return null;
            }

            return CompareVersions(cliVersion, maxKnownCliVersion) > 0
                ? null
                : InferProtocolVersionForCliVersion(cliVersion);
        }

        /// <summary>
        /// Semver-ish comparison: numeric core first, then "a prerelease sorts before its release".
        /// Returns 0 when either side cannot be parsed.
        /// </summary>
        public static int CompareVersions(string leftVersion, string rightVersion)
        {
            Version? leftCore;
            Version? rightCore;
            string leftPrerelease;
            string rightPrerelease;
            if (!TryParseVersion(leftVersion, out leftCore, out leftPrerelease)
                || !TryParseVersion(rightVersion, out rightCore, out rightPrerelease)
                || leftCore == null
                || rightCore == null)
            {
                return 0;
            }

            int coreComparison = leftCore.CompareTo(rightCore);
            if (coreComparison != 0)
            {
                return coreComparison;
            }

            bool leftIsPrerelease = leftPrerelease.Length > 0;
            bool rightIsPrerelease = rightPrerelease.Length > 0;
            if (leftIsPrerelease != rightIsPrerelease)
            {
                return leftIsPrerelease ? -1 : 1;
            }

            return leftIsPrerelease
                ? string.CompareOrdinal(leftPrerelease, rightPrerelease)
                : 0;
        }

        public static string NormalizeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("Version is required.", "version");
            }

            return version.Trim().TrimStart('v', 'V');
        }

        private static bool TryParseVersion(string version, out Version? core, out string prerelease)
        {
            core = null;
            prerelease = string.Empty;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            string normalized = version.Trim().TrimStart('v', 'V');
            int buildMetadataIndex = normalized.IndexOf('+');
            if (buildMetadataIndex >= 0)
            {
                normalized = normalized.Substring(0, buildMetadataIndex);
            }

            string coreVersion = normalized;
            int prereleaseIndex = normalized.IndexOf('-');
            if (prereleaseIndex >= 0)
            {
                coreVersion = normalized.Substring(0, prereleaseIndex);
                prerelease = normalized.Substring(prereleaseIndex + 1);
            }

            return Version.TryParse(coreVersion, out core);
        }
    }
}
