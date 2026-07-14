using System;
using System.IO;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal static class AssetCommandSupport
    {
        public static AssetRecord BuildRecordFromGuid(string guid)
        {
            string normalizedGuid = string.IsNullOrWhiteSpace(guid) ? string.Empty : guid.Trim();
            string path = string.IsNullOrWhiteSpace(normalizedGuid) ? string.Empty : AssetDatabase.GUIDToAssetPath(normalizedGuid);
            AssetRecord record = string.IsNullOrWhiteSpace(path)
                ? new AssetRecord
                {
                    guid = normalizedGuid,
                    exists = false,
                }
                : BuildRecordFromPath(path, allowPackages: true);

            if (string.IsNullOrWhiteSpace(record.guid))
            {
                record.guid = normalizedGuid;
            }

            return record;
        }

        public static AssetRecord BuildRecordFromPath(string path, bool allowPackages = false)
        {
            string normalizedPath = NormalizeAssetPath(path, allowPackages);
            bool isFolder = AssetDatabase.IsValidFolder(normalizedPath);
            string guid = AssetDatabase.AssetPathToGUID(normalizedPath);
            UnityEngine.Object mainAsset = isFolder ? null : AssetDatabase.LoadMainAssetAtPath(normalizedPath);
            bool hasExistingAsset = DoesAssetPathExistOnDisk(normalizedPath, allowPackages) || isFolder || mainAsset != null;

            string assetName;
            if (mainAsset != null)
            {
                assetName = mainAsset.name;
            }
            else if (isFolder)
            {
                assetName = Path.GetFileName(normalizedPath);
            }
            else
            {
                assetName = Path.GetFileNameWithoutExtension(normalizedPath);
            }

            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(normalizedPath);
            return new AssetRecord
            {
                path = normalizedPath,
                guid = guid ?? string.Empty,
                assetName = assetName ?? string.Empty,
                mainType = mainType == null ? string.Empty : mainType.Name,
                isFolder = isFolder,
                exists = hasExistingAsset,
            };
        }

        public static bool AssetExists(string path)
        {
            string normalizedPath = NormalizeAssetPath(path);
            return DoesAssetPathExistOnDisk(normalizedPath)
                || AssetDatabase.IsValidFolder(normalizedPath)
                || AssetDatabase.LoadMainAssetAtPath(normalizedPath) != null;
        }

        public static string RequireExistingAssetPath(string path, string commandName)
        {
            string normalizedPath = NormalizeAssetPath(path);
            if (!AssetExists(normalizedPath))
            {
                throw new InvalidOperationException(commandName + " 대상 asset이 없습니다: " + normalizedPath);
            }

            return normalizedPath;
        }

        public static string NormalizeAssetPath(string path, bool allowPackages = false)
        {
            return AssetPathUtility.Normalize(path, allowPackages);
        }

        public static void EnsureParentFolderExists(string path)
        {
            string parentPath = GetParentFolder(path);
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                throw new CommandFailureException("ASSET_CREATE_FAILED", "대상 폴더가 없습니다: " + parentPath);
            }
        }

        [Obsolete("Use AssetBackupTransaction.RunWithMovedBackup instead.")]
        public static bool DeleteIfTargetExists(string path, bool canOverwrite, string commandName)
        {
            if (!AssetExists(path))
            {
                return false;
            }

            if (!canOverwrite)
            {
                throw new InvalidOperationException(commandName + " 대상 경로가 이미 있습니다. 덮어쓰려면 --force를 사용하세요: " + path);
            }

            if (!AssetDatabase.DeleteAsset(path))
            {
                throw new InvalidOperationException("기존 asset를 지우지 못했습니다: " + path);
            }

            return true;
        }

        public static string BuildRenamedPath(string path, string newName)
        {
            string folder = GetParentFolder(path);
            string extension = AssetDatabase.IsValidFolder(path) ? string.Empty : Path.GetExtension(path);
            return folder + "/" + newName + extension;
        }

        public static string GetParentFolder(string path)
        {
            int separatorIndex = path.LastIndexOf('/');
            if (separatorIndex <= 0)
            {
                return "Assets";
            }

            return path.Substring(0, separatorIndex);
        }

        public static string GetPhysicalPath(string assetPath, bool allowPackages = false)
        {
            string normalizedPath = NormalizeAssetPath(assetPath, allowPackages);
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new InvalidOperationException("Unity 프로젝트 루트를 찾지 못했습니다.");
            }

            string canonicalProjectRoot = Path.GetFullPath(projectRoot);
            string relativePath = normalizedPath.Replace('/', Path.DirectorySeparatorChar);
            string physicalPath = Path.GetFullPath(Path.Combine(canonicalProjectRoot, relativePath));

            // `Packages/...` resolves through UPM: registry packages are symlinks into
            // `Library/PackageCache`, and `file:` packages point outside the project entirely.
            // Path.GetFullPath follows those links, so a physical containment check would reject
            // every valid package path. Normalize() already rejects `..` and absolute inputs.
            if (AssetPathUtility.IsPackagesPath(normalizedPath))
            {
                return physicalPath;
            }

            string allowedRoot = Path.GetFullPath(Path.Combine(canonicalProjectRoot, "Assets"));
            if (!AssetPathUtility.IsPhysicalPathWithinRoot(physicalPath, allowedRoot))
            {
                throw new InvalidOperationException("asset 경로가 Unity 프로젝트 asset 루트 밖을 가리킵니다: " + normalizedPath);
            }

            return physicalPath;
        }

        private static bool DoesAssetPathExistOnDisk(string assetPath, bool allowPackages = false)
        {
            string physicalPath = GetPhysicalPath(assetPath, allowPackages);
            return File.Exists(physicalPath) || Directory.Exists(physicalPath);
        }
    }
}
