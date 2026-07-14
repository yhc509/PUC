#nullable enable
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class AssetCommandSupportTests
    {
        [Test]
        public void GetPhysicalPath_PackagesPath_DoesNotThrow()
        {
            // UPM resolves Packages/<name> through a symlink into Library/PackageCache, and
            // Unity's Path.GetFullPath follows it. A physical containment check against
            // <project>/Packages therefore rejects every valid package path, which used to take
            // down `asset find` whenever a search term matched a package asset.
            Assert.DoesNotThrow(() => AssetCommandSupport.GetPhysicalPath(
                "Packages/com.yhc509.unity-cli-bridge/package.json",
                allowPackages: true));
        }

        [Test]
        public void GetPhysicalPath_PackagesPath_WithoutAllowPackages_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => AssetCommandSupport.GetPhysicalPath(
                "Packages/com.yhc509.unity-cli-bridge/package.json"));
        }

        [Test]
        public void GetPhysicalPath_AssetsPath_StaysWithinAssetsRoot()
        {
            string assetsRoot = Path.GetFullPath(Application.dataPath);

            string physicalPath = AssetCommandSupport.GetPhysicalPath("Assets/Some/Nested/File.asset");

            Assert.IsTrue(
                physicalPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Assets paths must still resolve inside the Assets root: " + physicalPath);
        }

        [TestCase("Assets/../ProjectSettings/ProjectVersion.txt")]
        [TestCase("Packages/../ProjectSettings/ProjectVersion.txt")]
        public void GetPhysicalPath_TraversalSegments_Throw(string assetPath)
        {
            Assert.Throws<InvalidOperationException>(
                () => AssetCommandSupport.GetPhysicalPath(assetPath, allowPackages: true));
        }

        [Test]
        public void GetPhysicalPath_AbsolutePath_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => AssetCommandSupport.GetPhysicalPath("/etc/hosts", allowPackages: true));
        }
    }
}
