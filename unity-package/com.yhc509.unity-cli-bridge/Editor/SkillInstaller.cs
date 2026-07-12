#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityCliBridge.Bridge.Editor
{
    internal enum SkillTarget
    {
        [InspectorName("Claude Code")]
        ClaudeCode = 0,
        [InspectorName("Codex")]
        Codex = 1,
    }

    internal enum SkillScope
    {
        [InspectorName("Project")]
        Project = 0,
        [InspectorName("Global")]
        Global = 1,
    }

    internal static class SkillInstaller
    {
        private const string SkillName = "unity-cli-bridge";
        private const string AgentsDirectoryName = "agents";
        private const string SkillMarkdownFileName = "SKILL.md";
        private const string PackageVersionPlaceholder = "{{PACKAGE_VERSION}}";
        private const string MetaFileExtension = ".meta";
        private const string PackageVersionMarkerPattern = "<!--\\s*unity-cli-bridge-package-version\\s*:\\s*(?<version>.*?)\\s*-->";

        internal static void Install(SkillTarget target, SkillScope scope)
        {
            SkillTemplateInfo templateInfo = GetSkillTemplateInfo();
            string destination = GetDestination(target, scope);
            bool includeAgents = target == SkillTarget.Codex;

            if (Directory.Exists(destination))
            {
                bool shouldOverwrite = EditorUtility.DisplayDialog(
                    "Overwrite Skill?",
                    "기존 스킬이 이미 설치되어 있습니다: " + destination + "\n덮어쓰시겠습니까?",
                    "Overwrite",
                    "Cancel");
                if (!shouldOverwrite)
                {
                    return;
                }

                Directory.Delete(destination, true);
            }

            Directory.CreateDirectory(destination);
            CopyDirectory(templateInfo.TemplateRoot, destination, includeAgents, templateInfo.PackageVersion);

            Debug.Log($"[SkillInstaller] Installed {target} skill ({scope}) to: {destination}");
        }

        internal static string GetDestination(SkillTarget target, SkillScope scope)
        {
            switch (scope)
            {
                case SkillScope.Project:
                    return GetProjectDestination(target);
                case SkillScope.Global:
                    return GetGlobalDestination(target);
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported skill scope.");
            }
        }

        internal static bool IsSkillInstalled(SkillTarget target, SkillScope scope)
        {
            try
            {
                return File.Exists(GetSkillMarkdownPath(target, scope));
            }
            catch
            {
                return false;
            }
        }

        internal static string? GetInstalledSkillVersion(SkillTarget target, SkillScope scope)
        {
            try
            {
                return TryReadPackageVersionMarker(GetSkillMarkdownPath(target, scope));
            }
            catch
            {
                return null;
            }
        }

        internal static string? ParsePackageVersionMarker(string skillMarkdown)
        {
            if (string.IsNullOrWhiteSpace(skillMarkdown))
            {
                return null;
            }

            Match match = Regex.Match(
                skillMarkdown,
                PackageVersionMarkerPattern,
                RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (!match.Success)
            {
                return null;
            }

            string version = match.Groups["version"].Value.Trim();
            return version.Length == 0 ? null : version;
        }

        internal static string? TryReadPackageVersionMarker(string skillMarkdownPath)
        {
            try
            {
                if (!File.Exists(skillMarkdownPath))
                {
                    return null;
                }

                return ParsePackageVersionMarker(File.ReadAllText(skillMarkdownPath));
            }
            catch
            {
                return null;
            }
        }

        private static string GetGlobalDestination(SkillTarget target)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                throw new InvalidOperationException("Failed to resolve user home directory.");
            }

            switch (target)
            {
                case SkillTarget.ClaudeCode:
                    return Path.Combine(home, ".claude", "skills", SkillName);
                case SkillTarget.Codex:
                    return Path.Combine(home, ".codex", "skills", SkillName);
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported skill target.");
            }
        }

        private static string GetProjectDestination(SkillTarget target)
        {
            DirectoryInfo? projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null || string.IsNullOrWhiteSpace(projectRoot.FullName))
            {
                throw new InvalidOperationException("Failed to resolve Unity project root from Application.dataPath: " + Application.dataPath);
            }

            switch (target)
            {
                case SkillTarget.ClaudeCode:
                    return Path.Combine(projectRoot.FullName, ".claude", "skills", SkillName);
                case SkillTarget.Codex:
                    return Path.Combine(projectRoot.FullName, ".codex", "skills", SkillName);
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported skill target.");
            }
        }

        private static string GetSkillMarkdownPath(SkillTarget target, SkillScope scope)
        {
            return Path.Combine(GetDestination(target, scope), SkillMarkdownFileName);
        }

        private static SkillTemplateInfo GetSkillTemplateInfo()
        {
            PackageManagerInfo? packageInfo = PackageManagerInfo.FindForAssembly(typeof(SkillInstaller).Assembly);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                throw new InvalidOperationException("Could not resolve the Unity package path.");
            }

            if (string.IsNullOrWhiteSpace(packageInfo.version))
            {
                throw new InvalidOperationException("Could not resolve the Unity package version.");
            }

            return new SkillTemplateInfo(
                Path.Combine(packageInfo.resolvedPath, "SkillTemplates~"),
                packageInfo.version.Trim());
        }

        private static void CopyDirectory(string source, string destination, bool includeAgents, string packageVersion)
        {
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("Skill template not found: " + source);
            }

            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
            {
                if (string.Equals(Path.GetExtension(file), MetaFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string destinationFile = Path.Combine(destination, Path.GetFileName(file));
                if (string.Equals(Path.GetFileName(file), SkillMarkdownFileName, StringComparison.OrdinalIgnoreCase))
                {
                    string skillMarkdown = File.ReadAllText(file);
                    File.WriteAllText(destinationFile, skillMarkdown.Replace(PackageVersionPlaceholder, packageVersion));
                }
                else
                {
                    File.Copy(file, destinationFile, overwrite: true);
                }
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                string directoryName = Path.GetFileName(directory);
                if (!includeAgents && string.Equals(directoryName, AgentsDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CopyDirectory(directory, Path.Combine(destination, directoryName), includeAgents, packageVersion);
            }
        }

        private readonly struct SkillTemplateInfo
        {
            internal SkillTemplateInfo(string templateRoot, string packageVersion)
            {
                TemplateRoot = templateRoot;
                PackageVersion = packageVersion;
            }

            internal string TemplateRoot { get; }
            internal string PackageVersion { get; }
        }
    }
}
