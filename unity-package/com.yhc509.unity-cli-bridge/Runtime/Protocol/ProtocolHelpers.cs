using System;

namespace UnityCli.Protocol
{
    public static class ProtocolHelpers
    {
        public static string[] GetSupportedCommands()
        {
            return CliCommandCatalog.GetSupportedProtocolCommands();
        }

        public static bool IsCommandAllowedWhileBusy(string command)
        {
            return CliCommandCatalog.IsCommandAllowedWhileBusy(command);
        }

        public static bool IsAssetCommand(string command)
        {
            return CliCommandCatalog.IsProtocolCommandInGroup(command, CliCommandGroup.AssetWorkflows);
        }

        public static bool IsSceneCommand(string command)
        {
            return CliCommandCatalog.IsProtocolCommandInGroup(command, CliCommandGroup.SceneWorkflows);
        }

        public static bool IsPrefabCommand(string command)
        {
            return CliCommandCatalog.IsProtocolCommandInGroup(command, CliCommandGroup.PrefabWorkflows);
        }

        public static bool IsPackageCommand(string command)
        {
            return CliCommandCatalog.IsProtocolCommandInGroup(command, CliCommandGroup.PackageManagement);
        }

        public static bool IsDeferredPackageCommand(string command)
        {
            return IsPackageCommand(command);
        }

        public static bool IsTestCommand(string command)
        {
            return string.Equals(command, ProtocolConstants.CommandTestList, StringComparison.Ordinal)
                || string.Equals(command, ProtocolConstants.CommandTestRun, StringComparison.Ordinal)
                || string.Equals(command, ProtocolConstants.CommandTestResults, StringComparison.Ordinal);
        }

        public static bool IsDeferredTestCommand(string command)
        {
            return string.Equals(command, ProtocolConstants.CommandTestList, StringComparison.Ordinal)
                || string.Equals(command, ProtocolConstants.CommandTestRun, StringComparison.Ordinal);
        }

        /// <summary>
        /// Run IDs are generated as <c>Guid.NewGuid().ToString("N")</c>, so anything else is
        /// rejected before it can reach a file path built from the ID.
        /// </summary>
        public static bool IsValidTestRunId(string runId)
        {
            if (string.IsNullOrEmpty(runId) || runId.Length != 32)
            {
                return false;
            }

            for (int index = 0; index < runId.Length; index++)
            {
                char character = runId[index];
                bool isHex = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TestFullNameMatchesFilter(string fullName, string substringFilter)
        {
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(substringFilter))
            {
                return false;
            }

            return fullName.IndexOf(substringFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string NormalizeManagedReferenceTypeNameForClrLookup(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return typeName;
            }

            int commaIndex = FindTopLevelComma(typeName);
            if (commaIndex >= 0)
            {
                return NormalizeTypeNamePart(typeName, 0, commaIndex)
                    + typeName.Substring(commaIndex);
            }

            int spaceIndex = typeName.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return typeName.Substring(0, spaceIndex + 1)
                    + NormalizeTypeNamePart(typeName, spaceIndex + 1, typeName.Length);
            }

            return typeName.Replace('/', '+');
        }

        public static bool IsTestRunResultStatusError(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return !string.Equals(status, "Completed", StringComparison.Ordinal)
                && !string.Equals(status, "Running", StringComparison.Ordinal)
                && !string.Equals(status, "STARTED", StringComparison.Ordinal);
        }

        public static string GetTestRunResultErrorCode(string status, string[] warnings)
        {
            if (string.Equals(status, "TimedOut", StringComparison.Ordinal))
            {
                return ProtocolConstants.ErrorTestTimeout;
            }

            if (string.Equals(status, "Cancelled", StringComparison.Ordinal))
            {
                return ProtocolConstants.ErrorTestCancelled;
            }

            if (string.Equals(status, "Failed", StringComparison.Ordinal)
                && ContainsWarning(warnings, ProtocolConstants.TestRunInterruptedMessage))
            {
                return ProtocolConstants.ErrorTestInterrupted;
            }

            if (string.Equals(status, "Failed", StringComparison.Ordinal))
            {
                return ProtocolConstants.ErrorTestRunFailed;
            }

            return ProtocolConstants.ErrorTestRunFailed;
        }

        public static string BuildTestRunResultErrorMessage(TestRunResultPayload result)
        {
            string runLabel = string.IsNullOrWhiteSpace(result.runId)
                ? "Test run"
                : "Test run " + result.runId;

            if (string.Equals(result.status, "TimedOut", StringComparison.Ordinal))
            {
                return runLabel + " timed out.";
            }

            if (string.Equals(result.status, "Cancelled", StringComparison.Ordinal))
            {
                return runLabel + " was cancelled.";
            }

            string warning = FirstWarning(result.warnings);
            if (!string.IsNullOrWhiteSpace(warning))
            {
                return warning;
            }

            return runLabel + " ended with status " + result.status + ".";
        }

        private static bool ContainsWarning(string[] warnings, string expected)
        {
            for (int index = 0; index < warnings.Length; index++)
            {
                if (string.Equals(warnings[index], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindTopLevelComma(string value)
        {
            int bracketDepth = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char currentCharacter = value[index];
                if (currentCharacter == '[')
                {
                    bracketDepth++;
                }
                else if (currentCharacter == ']')
                {
                    bracketDepth--;
                }
                else if (currentCharacter == ',' && bracketDepth == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string NormalizeTypeNamePart(string value, int startIndex, int endIndex)
        {
            return value.Substring(startIndex, endIndex - startIndex).Replace('/', '+');
        }

        private static string FirstWarning(string[] warnings)
        {
            for (int index = 0; index < warnings.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(warnings[index]))
                {
                    return warnings[index];
                }
            }

            return string.Empty;
        }
    }
}
