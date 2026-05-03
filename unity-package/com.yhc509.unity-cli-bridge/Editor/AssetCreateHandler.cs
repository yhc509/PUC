using System;
using System.IO;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed class AssetCreateHandler
    {
        public string Handle(string argumentsJson)
        {
            AssetCreateArgs args = ProtocolJson.Deserialize<AssetCreateArgs>(argumentsJson) ?? new AssetCreateArgs();
            IAssetCreateProvider provider = AssetCreateRegistry.Resolve(args.type);
            AssetCreateTypeDescriptor descriptor = provider.Describe();
            string createType = descriptor.typeId;
            string path = ResolveOutputPath(descriptor.defaultExtension, args.path);
            AssetCommandSupport.EnsureParentFolderExists(path);
            if (!args.force && AssetCommandSupport.AssetExists(path))
            {
                throw new CommandFailureException(ProtocolConstants.ErrorAssetForceRequired, "asset 덮어쓰기 또는 삭제에는 --force가 필요합니다.");
            }

            var request = new AssetCreateRequest(args, createType, path);
            bool isOverwritten = AssetCommandSupport.AssetExists(path);
            return AssetBackupTransaction.RunWithMovedBackup(path, "asset-create", () => CreateAsset(provider, request, descriptor, isOverwritten));
        }

        private static string CreateAsset(
            IAssetCreateProvider provider,
            AssetCreateRequest request,
            AssetCreateTypeDescriptor descriptor,
            bool isOverwritten)
        {
            string path = request.AssetPath;
            string createType = request.TypeId;
            AssetCreateArtifact artifact = provider.Create(request);
            ApplyDataPatchIfNeeded(descriptor, artifact, request);
            artifact.SaveAction();

            AssetDatabase.SaveAssets();
            // Unity 6+: SaveScene/SavePrefab 후 ImportAsset 대신 Refresh를 써야
            // file watcher가 변경을 동기 인식하여 "modified externally" 대화상자가 뜨지 않음.
            AssetDatabase.Refresh();
            ValidateCreatedAsset(path, artifact.ExpectedMainAssetType);

            return ProtocolJson.Serialize(new AssetCreatePayload
            {
                asset = AssetCommandSupport.BuildRecordFromPath(path),
                createdType = createType,
                overwritten = isOverwritten,
            });
        }

        public string HandleTypes()
        {
            return ProtocolJson.Serialize(new AssetTypesPayload
            {
                types = AssetCreateRegistry.GetDescriptors(),
            });
        }

        private static string ResolveOutputPath(string extension, string path)
        {
            string normalizedPath = AssetCommandSupport.NormalizeAssetPath(path);
            if (string.Equals(normalizedPath, "Assets", StringComparison.Ordinal))
            {
                throw new CommandFailureException("ASSET_CREATE_FAILED", "asset 파일 경로는 `Assets/...` 형식이어야 합니다.");
            }

            string currentExtension = Path.GetExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(currentExtension))
            {
                return normalizedPath + extension;
            }

            if (!string.Equals(currentExtension, extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandFailureException(
                    "ASSET_CREATE_FAILED",
                    "이 타입은 " + extension + " 확장자를 사용해야 합니다: " + normalizedPath);
            }

            return normalizedPath;
        }

        private static void ApplyDataPatchIfNeeded(AssetCreateTypeDescriptor descriptor, AssetCreateArtifact artifact, AssetCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DataJson))
            {
                return;
            }

            if (!descriptor.supportsDataPatch || artifact.DataPatchTarget == null)
            {
                throw new CommandFailureException("ASSET_DATA_PATCH_FAILED", request.TypeId + " 타입은 `--data-json`을 지원하지 않습니다.");
            }

            try
            {
                JsonUtility.FromJsonOverwrite(request.DataJson, artifact.DataPatchTarget);
            }
            catch (Exception exception)
            {
                throw new CommandFailureException(
                    "ASSET_DATA_PATCH_FAILED",
                    "초기값 JSON 적용에 실패했습니다: " + request.TypeId,
                    exception.Message);
            }
        }

        private static void ValidateCreatedAsset(string path, Type expectedMainAssetType)
        {
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (mainAsset == null)
            {
                throw new CommandFailureException("ASSET_CREATE_FAILED", "asset 생성 후 메인 asset을 읽지 못했습니다: " + path);
            }

            if (!expectedMainAssetType.IsInstanceOfType(mainAsset))
            {
                throw new CommandFailureException(
                    "ASSET_CREATE_FAILED",
                    "생성된 asset 타입이 예상과 다릅니다: " + path,
                    "expected=" + expectedMainAssetType.FullName + ", actual=" + mainAsset.GetType().FullName);
            }
        }
    }
}
