#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace UnityCli.Protocol
{
    public static class ProtocolConstants
    {
        public const string AppName = "unity-cli";
        public const int DefaultLiveTimeoutMs = 30_000;
        public const int DefaultTimeoutMs = DefaultLiveTimeoutMs;
        public const int DefaultConsoleLimit = 50;
        public const int DefaultAssetFindLimit = 50;
        public const int DefaultPackageRequestTimeoutSeconds = 300;
        public const int DefaultPackageLiveTimeoutMs = 360_000;
        public const int RegistryHeartbeatSeconds = 2;
        public const string BusyErrorCode = "BUSY";
        public const string ErrorAssetForceRequired = "ASSET_FORCE_REQUIRED";
        public const string ErrorExecuteForceRequired = "EXECUTE_FORCE_REQUIRED";
        public const string ErrorPrefabForceRequired = "PREFAB_FORCE_REQUIRED";
        public const string ErrorPackageForceRequired = "PACKAGE_FORCE_REQUIRED";
        public const string ErrorPackageBusy = "PACKAGE_BUSY";
        public const string PackageBusyMessage = "다른 패키지 명령이 진행 중입니다. 완료 후 다시 시도하세요.";
        public const string ErrorPackageTimeout = "PACKAGE_TIMEOUT";
        public const string ErrorSceneForceRequired = "SCENE_FORCE_REQUIRED";
        public const string ErrorBackupFailed = "BACKUP_FAILED";
        public const string ErrorBackupRestoreFailed = "BACKUP_RESTORE_FAILED";
        public const string ErrorSceneDirty = "SCENE_DIRTY";
        public const string StatusSuccess = "success";
        public const string StatusError = "error";
        public const string TransportLive = "live";
        public const string CommandPing = "ping";
        public const string CommandStatus = "status";
        public const string CommandRefresh = "refresh";
        public const string CommandCompile = "compile";
        public const string CommandPlay = "play";
        public const string CommandPause = "pause";
        public const string CommandStop = "stop";
        public const string CommandExecuteMenu = "execute-menu";
        public const string CommandScreenshot = "screenshot";
        public const string CommandExecuteCode = "execute-code";
        public const string CommandCustom = "custom";
        public const string CommandPackageList = "package-list";
        public const string CommandPackageAdd = "package-add";
        public const string CommandPackageRemove = "package-remove";
        public const string CommandPackageSearch = "package-search";
        public const string CommandMaterialInfo = "material-info";
        public const string CommandMaterialSet = "material-set";
        public const string CommandReadConsole = "read-console";
        public const string CommandAssetFind = "asset-find";
        public const string CommandAssetTypes = "asset-types";
        public const string CommandAssetInfo = "asset-info";
        public const string CommandAssetReimport = "asset-reimport";
        public const string CommandAssetMkdir = "asset-mkdir";
        public const string CommandAssetMove = "asset-move";
        public const string CommandAssetRename = "asset-rename";
        public const string CommandAssetDelete = "asset-delete";
        public const string CommandAssetCreate = "asset-create";
        public const string CommandSceneOpen = "scene-open";
        public const string CommandSceneInspect = "scene-inspect";
        public const string CommandScenePatch = "scene-patch";
        public const string CommandSceneSetTransform = "scene-set-transform";
        public const string CommandSceneAssignMaterial = "scene-assign-material";
        public const string CommandSceneListComponents = "scene-list-components";
        public const string CommandPrefabInspect = "prefab-inspect";
        public const string CommandPrefabCreate = "prefab-create";
        public const string CommandPrefabPatch = "prefab-patch";
        public const string CommandPrefabListComponents = "prefab-list-components";
        public const string CommandQaClick = "qa-click";
        public const string CommandQaTap = "qa-tap";
        public const string CommandQaSwipe = "qa-swipe";
        public const string CommandQaKey = "qa-key";
        public const string CommandQaWaitUntil = "qa-wait-until";
        public const int DefaultQaWaitUntilTimeoutMs = 10_000;
        public const int DefaultQaSwipeDurationMs = 300;
        public static readonly string[] SupportedScenePrimitiveNames =
        {
            "Cube",
            "Sphere",
            "Capsule",
            "Cylinder",
            "Plane",
            "Quad",
        };

        public static string NormalizeScenePrimitive(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "cube":
                    return "Cube";
                case "sphere":
                    return "Sphere";
                case "capsule":
                    return "Capsule";
                case "cylinder":
                    return "Cylinder";
                case "plane":
                    return "Plane";
                case "quad":
                    return "Quad";
                default:
                    return string.Empty;
            }
        }

        public static bool IsPackageRequestTimedOut(TimeSpan elapsed, int timeoutSeconds = DefaultPackageRequestTimeoutSeconds)
        {
            if (timeoutSeconds <= 0)
            {
                return true;
            }

            return elapsed.TotalSeconds >= timeoutSeconds;
        }

        public static string BuildPackageRequestTimeoutMessage(int timeoutSeconds)
        {
            return $"패키지 명령이 {timeoutSeconds}초 안에 완료되지 않았습니다. Editor의 Package Manager 상태를 확인해 주세요.";
        }

        public static string ComputeProjectHash(string projectRoot)
        {
            var normalized = GetCanonicalPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/')
                .ToLowerInvariant();

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    builder.Append(bytes[index].ToString("x2"));
                }

                return builder.ToString().Substring(0, 12);
            }
        }

        public static string GetCanonicalPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var realPath = TryResolveRealPath(fullPath);
            return (string.IsNullOrWhiteSpace(realPath) ? fullPath : realPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string BuildPipeName(string projectHash)
        {
            if (Path.DirectorySeparatorChar == '\\')
            {
                return $"unity-cli-{projectHash}";
            }

            return Path.Combine(Path.GetTempPath(), $"unity-cli-{projectHash}.sock");
        }

        private static string? TryResolveRealPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            if (Path.DirectorySeparatorChar == '\\')
            {
                return null;
            }

            if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
            {
                return null;
            }

            IntPtr resolvedPointer = RealPath(fullPath, IntPtr.Zero);
            if (resolvedPointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringAnsi(resolvedPointer);
            }
            finally
            {
                Free(resolvedPointer);
            }
        }

        [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPath(string path, IntPtr buffer);

        [DllImport("libc", EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);
    }
}
