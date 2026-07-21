#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace UnityCliBridge.Bridge.Editor
{
    /// <summary>
    /// Validation for the two untrusted inputs of the CLI install flow: the release
    /// download URL and the contents of the extracted staging directory.
    /// Deliberately free of UnityEditor/UnityEngine references so it compiles under
    /// plain .NET and can be unit tested.
    /// </summary>
    internal static class CliInstallGuard
    {
        /// <summary>
        /// Canonical repository location, and the single source of truth for the trusted
        /// download origin. <see cref="CliInstallerState"/> builds its release URLs from this.
        /// </summary>
        internal const string RepositoryUrl = "https://github.com/yhc509/unity-cli-bridge";

        /// <summary>Path segment every release asset lives under, relative to the repository.</summary>
        internal const string ReleaseDownloadPathSegment = "/releases/download/";

        /// <summary>
        /// Throws unless <paramref name="url"/> is an https URL pointing at this repository's
        /// release-download location.
        /// </summary>
        internal static void EnsureTrustedDownloadUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("Refusing to download the CLI: the download URL is empty.");
            }

            var repositoryUri = new Uri(RepositoryUrl);
            string expectedPathPrefix = repositoryUri.AbsolutePath + ReleaseDownloadPathSegment;

            Uri? requestedUri;
            bool isTrusted = Uri.TryCreate(url, UriKind.Absolute, out requestedUri)
                // Components are compared individually rather than as a raw string prefix:
                // a prefix compare is defeated by lookalike hosts ("github.com.evil.test"),
                // by userinfo ("https://github.com@evil.test/..." parses to host evil.test),
                // and by a non-default port. Uri also normalises "../" and "%2e%2e/" out of
                // the path before the prefix check sees it.
                && string.Equals(requestedUri!.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                && string.IsNullOrEmpty(requestedUri.UserInfo)
                && requestedUri.IsDefaultPort
                && string.Equals(requestedUri.Host, repositoryUri.Host, StringComparison.OrdinalIgnoreCase)
                && requestedUri.AbsolutePath.StartsWith(expectedPathPrefix, StringComparison.Ordinal)
                && requestedUri.AbsolutePath.Length > expectedPathPrefix.Length
                // What we validate has to be what we fetch. The caller downloads the raw
                // string, not this parsed Uri, and parsing hides the difference: a URL
                // carrying CR/LF or NUL arrives here already escaped to "%0D%0A" / "%00"
                // and reads as a clean path. Release asset paths never contain a percent
                // sign (semver "+build" metadata is not escaped), so refusing one closes
                // that gap outright. It also drops "%2f", which Uri does not decode and
                // which would otherwise slip a traversal past the prefix check.
                && requestedUri.AbsolutePath.IndexOf('%') < 0;

            if (!isTrusted)
            {
                throw new InvalidOperationException(
                    "Refusing to download the CLI from an untrusted URL: " + url
                    + " (expected an https URL under " + RepositoryUrl + ReleaseDownloadPathSegment + ").");
            }
        }

        /// <summary>
        /// Throws if any entry under <paramref name="rootDirectory"/> is a symbolic link.
        /// Extraction on macOS uses /usr/bin/tar, which refuses traversal and writes through
        /// existing symlinks but happily creates symlink entries themselves; a symlinked file
        /// or directory left in the staging tree would then be dereferenced by the copy into
        /// the install directory. Release archives ship a plain executable, so this rejects
        /// nothing legitimate.
        /// </summary>
        internal static void EnsureNoSymlinks(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Directory path is required.", nameof(rootDirectory));
            }

            string normalizedRoot = Path.GetFullPath(rootDirectory);
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(normalizedRoot);

            while (pendingDirectories.Count > 0)
            {
                string currentDirectory = pendingDirectories.Pop();

                // One directory level at a time. SearchOption.AllDirectories follows directory
                // symlinks, so a scan using it would walk an arbitrary tree outside staging.
                foreach (string entryPath in Directory.GetFileSystemEntries(currentDirectory))
                {
                    // Measured on both .NET and Unity's Mono: a dangling link reports
                    // ReparsePoint here rather than throwing, so it is rejected below like
                    // any other link. Left uncaught anyway — an entry whose attributes cannot
                    // be read must abort the install, never get copied out of staging.
                    FileAttributes attributes = File.GetAttributes(entryPath);

                    if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    {
                        throw new InvalidOperationException(
                            "Refusing to install the CLI: the downloaded archive contains a symbolic link at '"
                            + ToRelativePath(normalizedRoot, entryPath) + "'.");
                    }

                    if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        pendingDirectories.Push(entryPath);
                    }
                }
            }
        }

        private static string ToRelativePath(string rootDirectory, string fullPath)
        {
            // Always strictly longer than the root: every path here came out of
            // Directory.GetFileSystemEntries on a directory at or below it.
            return fullPath
                .Substring(rootDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
