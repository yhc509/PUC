using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class PrefabCommandHandler
    {
        public bool CanHandle(string command)
        {
            return ProtocolHelpers.IsPrefabCommand(command);
        }

        public string Handle(string command, string argumentsJson)
        {
            switch (command)
            {
                case ProtocolConstants.CommandPrefabInspect:
                    return HandleInspect(argumentsJson);
                case ProtocolConstants.CommandPrefabCreate:
                    return HandleCreate(argumentsJson);
                case ProtocolConstants.CommandPrefabPatch:
                    return HandlePatch(argumentsJson);
                case ProtocolConstants.CommandPrefabListComponents:
                    return HandleListComponents(argumentsJson);
                default:
                    throw new InvalidOperationException("지원하지 않는 prefab 명령입니다: " + command);
            }
        }

        private static string HandleInspect(string argumentsJson)
        {
            JObject arguments = InspectorMutationReaderUtility.ParseArgumentsObject(argumentsJson);
            PrefabInspectArgs args = InspectorMutationReaderUtility.DeserializeArguments<PrefabInspectArgs>(arguments);
            int? maxDepth = args.maxDepth ?? InspectorMutationReaderUtility.ParseOptionalMaxDepth(arguments, "PREFAB_INSPECT_INVALID");
            string path = RequireExistingPrefabPath(args.path, "prefab-inspect");
            if (maxDepth.HasValue && maxDepth.Value <= 0)
            {
                throw new CommandFailureException("PREFAB_INSPECT_INVALID", "`--max-depth`는 1 이상의 정수여야 합니다.");
            }
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                return PrefabInspector.BuildInspectPayload(path, root, args.withValues, maxDepth, args.omitDefaults);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string HandleCreate(string argumentsJson)
        {
            PrefabCreateArgs args = ProtocolJson.Deserialize<PrefabCreateArgs>(argumentsJson) ?? new PrefabCreateArgs();
            string path = ResolvePrefabPath(args.path);
            AssetCommandSupport.EnsureParentFolderExists(path);

            PrefabCreateSpec spec = DeserializeSpec<PrefabCreateSpec>(args.specJson, "prefab-create");
            ValidateVersion(spec.Version, "prefab-create");
            if (spec.Root == null)
            {
                throw new CommandFailureException("PREFAB_SPEC_INVALID", "`root`가 필요합니다.");
            }

            bool isOverwritten = AssetCommandSupport.AssetExists(path);
            if (isOverwritten && !args.force)
            {
                throw new CommandFailureException(ProtocolConstants.ErrorPrefabForceRequired, "prefab 덮어쓰기에는 --force가 필요합니다.");
            }

            if (isOverwritten)
            {
                EnsureCurrentPrefabStageCleanForWrite(path);
            }

            // Non-overwrite creates still use the transaction so failed saves remove partial body/.meta artifacts.
            return AssetBackupTransaction.RunWithMovedBackup(path, "prefab-create", () => CreatePrefab(path, spec, isOverwritten));
        }

        private static string CreatePrefab(string path, PrefabCreateSpec spec, bool isOverwritten)
        {
            string rootName = string.IsNullOrWhiteSpace(spec.Root.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : spec.Root.Name.Trim();
            var root = new GameObject(rootName);

            try
            {
                PrefabInspector.ApplyNodeState(root, spec.Root, rootName, allowMissingName: true);
                AddComponents(root, spec.Root.Components, "prefab-create");
                AddChildren(root.transform, spec.Root.Children, "prefab-create");

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (saved == null)
                {
                    throw new CommandFailureException("PREFAB_SAVE_FAILED", "prefab을 저장하지 못했습니다: " + path);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            // Unity 6+: SavePrefab 후 ImportAsset 대신 Refresh를 써야
            // file watcher가 변경을 동기 인식하여 "modified externally" 대화상자가 뜨지 않음.
            AssetDatabase.Refresh();
            return ProtocolJson.Serialize(new PrefabMutationPayload
            {
                asset = AssetCommandSupport.BuildRecordFromPath(path),
                created = true,
                overwritten = isOverwritten,
            });
        }

        private static string HandlePatch(string argumentsJson)
        {
            PrefabPatchArgs args = ProtocolJson.Deserialize<PrefabPatchArgs>(argumentsJson) ?? new PrefabPatchArgs();
            string path = RequireExistingPrefabPath(args.path, "prefab-patch");

            PrefabPatchSpec spec = DeserializeSpec<PrefabPatchSpec>(args.specJson, "prefab-patch");
            ValidateVersion(spec.Version, "prefab-patch");
            if (spec.Operations == null || spec.Operations.Length == 0)
            {
                throw new CommandFailureException("PREFAB_SPEC_INVALID", "`operations`가 비어 있습니다.");
            }

            NormalizeOperationNames(spec.Operations);
            if (HasDestructiveOperation(spec) && !args.force)
            {
                throw new CommandFailureException(ProtocolConstants.ErrorPrefabForceRequired, "`remove-node` 또는 `remove-component`를 쓰려면 --force가 필요합니다.");
            }

            EnsureCurrentPrefabStageCleanForWrite(path);
            return AssetBackupTransaction.RunWithBackup(path, "prefab-patch", () => PatchPrefab(path, spec));
        }

        private static string PatchPrefab(string path, PrefabPatchSpec spec)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            PrefabPatchApplyResult patchResult;
            try
            {
                patchResult = ApplyPatchOperations(root, spec.Operations);
                if (!patchResult.Patched)
                {
                    return ProtocolJson.Serialize(new PrefabMutationPayload
                    {
                        asset = AssetCommandSupport.BuildRecordFromPath(path),
                        patched = false,
                        warnings = patchResult.Warnings.Count == 0 ? null : patchResult.Warnings.ToArray(),
                    });
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (saved == null)
                {
                    throw new CommandFailureException("PREFAB_SAVE_FAILED", "prefab을 저장하지 못했습니다: " + path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            // Unity 6+: SavePrefab 후 ImportAsset 대신 Refresh를 써야
            // file watcher가 변경을 동기 인식하여 "modified externally" 대화상자가 뜨지 않음.
            AssetDatabase.Refresh();
            return ProtocolJson.Serialize(new PrefabMutationPayload
            {
                asset = AssetCommandSupport.BuildRecordFromPath(path),
                patched = patchResult.Patched,
                warnings = patchResult.Warnings.Count == 0 ? null : patchResult.Warnings.ToArray(),
            });
        }

        private static string HandleListComponents(string argumentsJson)
        {
            var args = ProtocolJson.Deserialize<PrefabListComponentsArgs>(argumentsJson)
                ?? new PrefabListComponentsArgs();
            string path = RequireExistingPrefabPath(args.path, "prefab-list-components");
            if (string.IsNullOrWhiteSpace(args.node))
            {
                throw new CommandFailureException("PREFAB_LIST_COMPONENTS_INVALID", "`--node`가 필요합니다.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                GameObject target = PrefabInspector.ResolveNode(root, args.node, "prefab-list-components");
                ComponentOperations.ComponentEntry[] entries = ComponentOperations.ListComponentEntries(target);
                return ProtocolJson.Serialize(new PrefabListComponentsPayload
                {
                    node = args.node,
                    components = entries,
                });
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T DeserializeSpec<T>(string specJson, string commandName) where T : class
        {
            if (string.IsNullOrWhiteSpace(specJson))
            {
                throw new CommandFailureException("PREFAB_SPEC_INVALID", commandName + " spec이 비어 있습니다.");
            }

            try
            {
                T spec = JsonConvert.DeserializeObject<T>(specJson, BridgeJsonSettings.CamelCaseIgnoreNull);

                if (spec == null)
                {
                    throw new CommandFailureException("PREFAB_SPEC_INVALID", commandName + " spec을 해석하지 못했습니다.");
                }

                return spec;
            }
            catch (JsonException exception)
            {
                throw new CommandFailureException("PREFAB_SPEC_INVALID", commandName + " spec JSON이 잘못되었습니다.", exception.Message);
            }
        }

        private static void ValidateVersion(int version, string commandName)
        {
            if (version > 1)
            {
                throw new CommandFailureException("PREFAB_SPEC_INVALID", commandName + " spec version을 지원하지 않습니다: " + version);
            }
        }

        private static string ResolvePrefabPath(string? path)
        {
            string normalizedPath = AssetCommandSupport.NormalizeAssetPath(path ?? string.Empty);
            string extension = Path.GetExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return normalizedPath + ".prefab";
            }

            if (!string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandFailureException("PREFAB_PATH_INVALID", "prefab 경로는 `.prefab` 확장자를 사용해야 합니다: " + normalizedPath);
            }

            return normalizedPath;
        }

        private static void EnsureCurrentPrefabStageCleanForWrite(string prefabPath)
        {
            PrefabStage? prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || !prefabStage.scene.isDirty)
            {
                return;
            }

            string normalizedPath = AssetCommandSupport.NormalizeAssetPath(prefabPath);
            string stageGuid = AssetDatabase.AssetPathToGUID(prefabStage.assetPath);
            string targetGuid = AssetDatabase.AssetPathToGUID(normalizedPath);
            bool isSamePrefab = !string.IsNullOrEmpty(stageGuid)
                && !string.IsNullOrEmpty(targetGuid)
                && string.Equals(stageGuid, targetGuid, StringComparison.Ordinal);
            if (!isSamePrefab)
            {
                return;
            }

            throw new CommandFailureException(
                ProtocolConstants.ErrorPrefabStageDirty,
                "'" + normalizedPath + "' prefab이 Prefab Stage에서 저장하지 않은 변경과 함께 열려 있어 진행할 수 없습니다. Prefab Stage에서 저장하거나 폐기한 뒤 다시 시도하세요.");
        }

        private static string RequireExistingPrefabPath(string? path, string commandName)
        {
            string prefabPath = ResolvePrefabPath(path);
            prefabPath = AssetCommandSupport.RequireExistingAssetPath(prefabPath, commandName);
            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(prefabPath);
            if (mainType != typeof(GameObject))
            {
                throw new CommandFailureException("PREFAB_PATH_INVALID", "prefab asset이 아닙니다: " + prefabPath);
            }

            return prefabPath;
        }
    }
}
