using System;

namespace UnityCli.Protocol
{
    /// <summary>
    /// Opt-out switch for starting the bridge at all.
    ///
    /// The bridge boots from <c>[InitializeOnLoad]</c> in every main editor process, headless
    /// included — which is the right default for interactive and CLI-driven work, but wrong for a
    /// CI/release build job. There the editor is launched only to produce a player, and a bridge
    /// that binds a socket, publishes a registry entry and a token sidecar, and then logs watchdog
    /// errors when any of that fails is pure noise on the build log (and a lock to contend over
    /// when several builds share a machine). A build job sets the environment variable or passes
    /// the command-line flag and gets an editor that never advertises itself.
    ///
    /// Kept free of Unity types so the parsing is unit-testable.
    /// </summary>
    internal static class BridgeDisableSwitch
    {
        /// <summary>Set to any value other than <c>0</c>/<c>false</c>/empty to keep the bridge down.</summary>
        internal const string EnvironmentVariable = "UNITY_CLI_BRIDGE_DISABLE";

        /// <summary>Editor command-line equivalent, for jobs that cannot set an environment variable.</summary>
        internal const string CommandLineFlag = "-noUnityCliBridge";

        /// <param name="environmentValue">Raw value of <see cref="EnvironmentVariable"/>; null when unset.</param>
        /// <param name="commandLineArgs">Editor argv; null is treated as empty.</param>
        internal static bool IsDisabled(string environmentValue, string[] commandLineArgs)
        {
            if (IsTruthy(environmentValue))
            {
                return true;
            }

            if (commandLineArgs != null)
            {
                for (int index = 0; index < commandLineArgs.Length; index++)
                {
                    if (string.Equals(commandLineArgs[index], CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Anything set counts as "on" except the two spellings people use to mean "off". A CI
        /// system that exports the variable unconditionally as <c>0</c> must not disable the bridge
        /// for everyone downstream.
        /// </summary>
        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            return !string.Equals(trimmed, "0", StringComparison.Ordinal)
                && !string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
