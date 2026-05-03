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
    }
}
