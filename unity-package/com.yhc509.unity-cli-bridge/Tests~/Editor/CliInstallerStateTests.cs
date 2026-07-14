#nullable enable
using NUnit.Framework;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class CliInstallerStateTests
    {
        [TestCase("0.3.5", "0.3.4", 1)]
        [TestCase("0.3.5", "0.3.5", 0)]
        [TestCase("0.3.5", "0.3.6", -1)]
        [TestCase("v0.3.5", "0.3.5", 0)]
        [TestCase("V0.3.5", "v0.3.4", 1)]
        [TestCase("0.3.5-gghf2", "0.3.5", -1)]
        [TestCase("0.4.0-rc1", "0.4.0", -1)]
        [TestCase("0.4.0", "0.4.0-rc1", 1)]
        [TestCase("0.4.0-rc1", "0.4.0-rc2", -1)]
        [TestCase("0.4.0+build.1", "0.4.0", 0)]
        [TestCase("0.4.0+build.1", "0.4.0+build.2", 0)]
        [TestCase("0.4.0-rc1+build.1", "0.4.0-rc1+build.2", 0)]
        public void CompareVersions_OrdersSupportedVersionForms(string left, string right, int expectedSign)
        {
            int comparison = CliInstallerState.CompareVersions(left, right);

            Assert.AreEqual(expectedSign, Sign(comparison));
        }

        [TestCase("not-a-version", "0.4.0")]
        [TestCase("0.4.0", "not-a-version")]
        [TestCase("", "0.4.0")]
        [TestCase("0.4.bad", "0.4.0")]
        public void CompareVersions_MalformedInput_DoesNotThrow(string left, string right)
        {
            Assert.DoesNotThrow(() => CliInstallerState.CompareVersions(left, right));
            Assert.AreEqual(0, CliInstallerState.CompareVersions(left, right));
        }

        private static int Sign(int value)
        {
            if (value < 0)
            {
                return -1;
            }

            if (value > 0)
            {
                return 1;
            }

            return 0;
        }
    }
}
