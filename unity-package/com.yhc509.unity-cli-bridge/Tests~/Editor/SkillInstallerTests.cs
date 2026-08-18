#nullable enable
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class SkillInstallerTests
    {
        [TestCase(SkillTarget.ClaudeCode, SkillScope.Project, ".claude")]
        [TestCase(SkillTarget.Codex, SkillScope.Project, ".codex")]
        [TestCase(SkillTarget.GrokBuild, SkillScope.Project, ".grok")]
        [TestCase(SkillTarget.ClaudeCode, SkillScope.Global, ".claude")]
        [TestCase(SkillTarget.Codex, SkillScope.Global, ".codex")]
        [TestCase(SkillTarget.GrokBuild, SkillScope.Global, ".grok")]
        public void GetDestination_UsesExpectedTargetAndScopePath(SkillTarget target, SkillScope scope, string toolDirectoryName)
        {
            string root = scope == SkillScope.Project
                ? GetProjectRoot()
                : GetHomeDirectory();
            string expected = Path.Combine(root, toolDirectoryName, "skills", "unity-cli-bridge");

            string actual = SkillInstaller.GetDestination(target, scope);

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void GetDestination_SupportsEverySkillTarget()
        {
            foreach (SkillTarget target in Enum.GetValues(typeof(SkillTarget)))
            {
                Assert.DoesNotThrow(() => SkillInstaller.GetDestination(target, SkillScope.Project));
                Assert.DoesNotThrow(() => SkillInstaller.GetDestination(target, SkillScope.Global));
            }
        }

        [Test]
        public void ParsePackageVersionMarker_ReturnsVersion()
        {
            string? version = SkillInstaller.ParsePackageVersionMarker(
                "<!-- unity-cli-bridge-package-version: 0.4.0 -->\n# Unity CLI Bridge");

            Assert.AreEqual("0.4.0", version);
        }

        [Test]
        public void ParsePackageVersionMarker_ReturnsNullWhenMarkerMissing()
        {
            string? version = SkillInstaller.ParsePackageVersionMarker("# Unity CLI Bridge");

            Assert.IsNull(version);
        }

        [Test]
        public void ParsePackageVersionMarker_AcceptsWhitespaceVariants()
        {
            string? version = SkillInstaller.ParsePackageVersionMarker(
                "<!--unity-cli-bridge-package-version:0.4.0-->");

            Assert.AreEqual("0.4.0", version);
        }

        [Test]
        public void TryReadPackageVersionMarker_ReturnsNullWhenFileMissing()
        {
            string missingPath = Path.Combine(
                Path.GetTempPath(),
                "unity-cli-bridge-test-" + Guid.NewGuid().ToString("N"),
                "SKILL.md");

            string? version = SkillInstaller.TryReadPackageVersionMarker(missingPath);

            Assert.IsNull(version);
        }

        private static string GetProjectRoot()
        {
            DirectoryInfo? projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException("Test project root could not be resolved.");
            }

            return projectRoot.FullName;
        }

        private static string GetHomeDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                throw new InvalidOperationException("Test home directory could not be resolved.");
            }

            return home;
        }
    }
}
