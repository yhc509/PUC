#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class SceneCommandHandler
    {
        public bool CanHandle(string command)
        {
            return ProtocolHelpers.IsSceneCommand(command);
        }

        public string Handle(string command, string argumentsJson)
        {
            switch (command)
            {
                case ProtocolConstants.CommandSceneOpen:
                    return HandleOpen(argumentsJson);
                case ProtocolConstants.CommandSceneInspect:
                    return HandleInspect(argumentsJson);
                case ProtocolConstants.CommandScenePatch:
                    return HandlePatch(argumentsJson);
                case ProtocolConstants.CommandSceneSetTransform:
                    return HandleSetTransform(argumentsJson);
                case ProtocolConstants.CommandSceneAssignMaterial:
                    return HandleAssignMaterial(argumentsJson);
                case ProtocolConstants.CommandSceneListComponents:
                    return HandleListComponents(argumentsJson);
                default:
                    throw new InvalidOperationException("지원하지 않는 scene 명령입니다: " + command);
            }
        }

        private static string HandleOpen(string argumentsJson)
        {
            SceneOpenArgs args = ProtocolJson.Deserialize<SceneOpenArgs>(argumentsJson) ?? new SceneOpenArgs();
            string path = RequireExistingScenePath(args.path, "scene-open");

            PrepareForOpen(args.force);

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new CommandFailureException("SCENE_OPEN_FAILED", "scene를 열지 못했습니다: " + path);
            }

            SceneManager.SetActiveScene(scene);
            return ProtocolJson.Serialize(new SceneOpenPayload
            {
                asset = AssetCommandSupport.BuildRecordFromPath(path),
                activeScenePath = scene.path,
                opened = true,
            });
        }

        private static string HandleInspect(string argumentsJson)
        {
            JObject arguments = InspectorMutationReaderUtility.ParseArgumentsObject(argumentsJson);
            SceneInspectArgs args = InspectorMutationReaderUtility.DeserializeArguments<SceneInspectArgs>(arguments);
            int? maxDepth = args.maxDepth ?? InspectorMutationReaderUtility.ParseOptionalMaxDepth(arguments, "SCENE_INSPECT_INVALID");
            string path = RequireExistingScenePath(args.path, "scene-inspect");
            if (maxDepth.HasValue && maxDepth.Value <= 0)
            {
                throw new CommandFailureException("SCENE_INSPECT_INVALID", "`--max-depth`는 1 이상의 정수여야 합니다.");
            }

            return WithLoadedScene(path, "scene-inspect", scene =>
                SceneInspector.BuildInspectPayload(path, scene, args.withValues, maxDepth, args.omitDefaults, EditorSceneManager.GetActiveScene().path));
        }

        private static string HandlePatch(string argumentsJson)
        {
            ScenePatchArgs args = ProtocolJson.Deserialize<ScenePatchArgs>(argumentsJson) ?? new ScenePatchArgs();
            string path = RequireExistingScenePath(args.path, "scene-patch");

            ScenePatchSpec spec = DeserializeSpec<ScenePatchSpec>(args.specJson, "scene-patch");
            ValidateVersion(spec.Version, "scene-patch");
            if (spec.Operations == null || spec.Operations.Length == 0)
            {
                throw new CommandFailureException("SCENE_SPEC_INVALID", "`operations`가 비어 있습니다.");
            }

            NormalizeOperationNames(spec.Operations);
            if (HasDestructiveOperation(spec) && !args.force)
            {
                throw new CommandFailureException(ProtocolConstants.ErrorSceneForceRequired, "`delete-gameobject` 또는 `remove-component`를 쓰려면 --force가 필요합니다.");
            }

            return WithLoadedScene(path, "scene-patch", delegate(Scene scene)
            {
                ScenePatchApplyResult patchResult = ApplyPatchOperations(scene, spec.Operations);
                if (!patchResult.Patched)
                {
                    return ProtocolJson.Serialize(new SceneMutationPayload
                    {
                        asset = AssetCommandSupport.BuildRecordFromPath(path),
                        activeScenePath = EditorSceneManager.GetActiveScene().path,
                        patched = false,
                        createdPath = patchResult.CreatedPath,
                        warnings = patchResult.Warnings.Count == 0 ? null : patchResult.Warnings.ToArray(),
                    });
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new CommandFailureException("SCENE_SAVE_FAILED", "scene를 저장하지 못했습니다: " + path);
                }
                // Unity 6+: SaveScene 후 ImportAsset 대신 Refresh를 써야
                // file watcher가 변경을 동기 인식하여 "modified externally" 대화상자가 뜨지 않음.
                AssetDatabase.Refresh();
                return ProtocolJson.Serialize(new SceneMutationPayload
                {
                    asset = AssetCommandSupport.BuildRecordFromPath(path),
                    activeScenePath = EditorSceneManager.GetActiveScene().path,
                    patched = patchResult.Patched,
                    createdPath = patchResult.CreatedPath,
                    warnings = patchResult.Warnings.Count == 0 ? null : patchResult.Warnings.ToArray(),
                });
            });
        }

        private static string HandleAssignMaterial(string argumentsJson)
        {
            SceneAssignMaterialArgs args = ProtocolJson.Deserialize<SceneAssignMaterialArgs>(argumentsJson) ?? new SceneAssignMaterialArgs();
            Scene scene = RequireActiveSavedScene("scene assign-material");
            GameObject node = SceneInspector.ResolveNode(scene, args.node, "scene assign-material");

            MeshRenderer meshRenderer = node.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                throw new CommandFailureException("SCENE_COMPONENT_NOT_FOUND", "scene assign-material 대상 node에 MeshRenderer가 없습니다: " + SceneInspector.BuildNodePath(node));
            }

            (Material material, string materialPath) = LoadMaterialAsset(args.material, "scene assign-material");
            Material[] sharedMaterials = meshRenderer.sharedMaterials ?? Array.Empty<Material>();
            string? previousMaterialPath = sharedMaterials.Length > 0 && sharedMaterials[0] != null
                ? AssetDatabase.GetAssetPath(sharedMaterials[0])
                : null;

            if (sharedMaterials.Length == 0)
            {
                sharedMaterials = new Material[1];
            }

            sharedMaterials[0] = material;
            meshRenderer.sharedMaterials = sharedMaterials;

            EditorUtility.SetDirty(meshRenderer);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new CommandFailureException("SCENE_SAVE_FAILED", "scene를 저장하지 못했습니다: " + scene.path);
            }

            AssetDatabase.Refresh();
            return ProtocolJson.Serialize(new SceneAssignMaterialPayload
            {
                asset = AssetCommandSupport.BuildRecordFromPath(scene.path),
                activeScenePath = scene.path,
                node = SceneInspector.BuildNodePath(node),
                material = materialPath,
                previousMaterial = previousMaterialPath,
            });
        }

        private static string HandleListComponents(string argumentsJson)
        {
            SceneListComponentsArgs args = ProtocolJson.Deserialize<SceneListComponentsArgs>(argumentsJson)
                ?? new SceneListComponentsArgs();
            if (string.IsNullOrWhiteSpace(args.node))
            {
                throw new CommandFailureException("SCENE_LIST_COMPONENTS_INVALID", "`--node`가 필요합니다.");
            }

            Scene scene = RequireActiveSavedScene("scene-list-components");
            GameObject target = SceneInspector.ResolveNode(scene, args.node, "scene-list-components");
            ComponentOperations.ComponentEntry[] entries = ComponentOperations.ListComponentEntries(target);

            return ProtocolJson.Serialize(new SceneListComponentsPayload
            {
                node = args.node,
                components = entries,
            });
        }

        private static string HandleSetTransform(string argumentsJson)
        {
            SceneSetTransformArgs args = ProtocolJson.Deserialize<SceneSetTransformArgs>(argumentsJson) ?? new SceneSetTransformArgs();
            if (args.position == null && args.rotation == null && args.scale == null)
            {
                throw new CommandFailureException("SCENE_TRANSFORM_INVALID", "scene set-transform에는 position, rotation, scale 중 하나 이상이 필요합니다.");
            }

            Scene scene = RequireActiveSavedScene("scene set-transform");
            GameObject node = SceneInspector.ResolveNode(scene, args.node, "scene set-transform");
            ApplySerializedTransform(node.transform, args.position, args.rotation, args.scale);

            EditorUtility.SetDirty(node.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new CommandFailureException("SCENE_SAVE_FAILED", "scene를 저장하지 못했습니다: " + scene.path);
            }

            AssetDatabase.Refresh();
            return ProtocolJson.Serialize(new SceneSetTransformPayload
            {
                asset = AssetCommandSupport.BuildRecordFromPath(scene.path),
                activeScenePath = scene.path,
                node = SceneInspector.BuildNodePath(node),
                position = args.position,
                rotation = args.rotation,
                scale = args.scale,
            });
        }

        private static T WithLoadedScene<T>(string path, string commandName, Func<Scene, T> action)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool isOpenedHere = false;

            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty)
                {
                    throw new CommandFailureException(
                        "SCENE_DIRTY",
                        commandName + " 대상 scene에 저장되지 않은 변경이 있습니다. 먼저 저장하거나 변경을 버리세요: " + path);
                }
            }
            else
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                isOpenedHere = true;
            }

            try
            {
                return action(scene);
            }
            finally
            {
                if (isOpenedHere && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void PrepareForOpen(bool isForced)
        {
            bool hasDirtyScene = false;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && scene.isDirty)
                {
                    hasDirtyScene = true;
                    if (!isForced)
                    {
                        string sceneName = string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path;
                        throw new CommandFailureException(
                            "SCENE_DIRTY",
                            "저장되지 않은 scene 변경이 있습니다. 버리고 열려면 --force를 사용하세요: " + sceneName);
                    }
                }
            }

            if (isForced && hasDirtyScene)
            {
                // Reset to a fresh scene first so `scene open --force` discards dirty state deterministically.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static T DeserializeSpec<T>(string? specJson, string commandName) where T : class
        {
            if (string.IsNullOrWhiteSpace(specJson))
            {
                throw new CommandFailureException("SCENE_SPEC_INVALID", commandName + " spec이 비어 있습니다.");
            }

            try
            {
                T spec = JsonConvert.DeserializeObject<T>(specJson, BridgeJsonSettings.CamelCaseIgnoreNull);

                if (spec == null)
                {
                    throw new CommandFailureException("SCENE_SPEC_INVALID", commandName + " spec을 해석하지 못했습니다.");
                }

                return spec;
            }
            catch (JsonException exception)
            {
                throw new CommandFailureException("SCENE_SPEC_INVALID", commandName + " spec JSON이 잘못되었습니다.", exception.Message);
            }
        }

        private static void ValidateVersion(int version, string commandName)
        {
            if (version > 1)
            {
                throw new CommandFailureException("SCENE_SPEC_INVALID", commandName + " spec version을 지원하지 않습니다: " + version);
            }
        }

        private static Scene RequireActiveSavedScene(string commandName)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new CommandFailureException("SCENE_NOT_OPEN", commandName + " 대상 active scene이 없습니다.");
            }

            if (string.IsNullOrWhiteSpace(scene.path))
            {
                throw new CommandFailureException("SCENE_PATH_INVALID", commandName + " 대상 active scene이 저장되지 않았습니다. 먼저 scene asset으로 저장하세요.");
            }

            return scene;
        }

        private static (Material material, string path) LoadMaterialAsset(string? path, string commandName)
        {
            string normalizedPath;
            try
            {
                normalizedPath = AssetCommandSupport.NormalizeAssetPath(path);
            }
            catch (Exception exception)
            {
                throw new CommandFailureException("SCENE_MATERIAL_INVALID", exception.Message, exception.ToString());
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedPath);
            if (material == null)
            {
                throw new CommandFailureException("MATERIAL_NOT_FOUND", commandName + " 머티리얼을 찾지 못했습니다: " + normalizedPath);
            }

            return (material, normalizedPath);
        }

        private static void ApplySerializedTransform(Transform transform, SceneVector3Value? position, SceneVector3Value? rotation, SceneVector3Value? scale)
        {
            var serializedObject = new SerializedObject(transform);
            serializedObject.Update();

            if (position != null)
            {
                RequireSerializedProperty(serializedObject, "m_LocalPosition").vector3Value = ToVector3(position);
            }

            if (rotation != null)
            {
                Vector3 euler = ToVector3(rotation);
                RequireSerializedProperty(serializedObject, "m_LocalRotation").quaternionValue = Quaternion.Euler(euler);

                SerializedProperty eulerHint = serializedObject.FindProperty("m_LocalEulerAnglesHint");
                if (eulerHint != null)
                {
                    eulerHint.vector3Value = euler;
                }
            }

            if (scale != null)
            {
                RequireSerializedProperty(serializedObject, "m_LocalScale").vector3Value = ToVector3(scale);
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty RequireSerializedProperty(SerializedObject serializedObject, string propertyPath)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                throw new CommandFailureException("SCENE_COMPONENT_INVALID", "Transform serialized field를 찾지 못했습니다: " + propertyPath);
            }

            return property;
        }

        private static Vector3 ToVector3(SceneVector3Value value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static string ResolveScenePath(string? path)
        {
            string normalizedPath;
            try
            {
                normalizedPath = AssetCommandSupport.NormalizeAssetPath(path);
            }
            catch (Exception exception)
            {
                throw new CommandFailureException("SCENE_PATH_INVALID", exception.Message, exception.ToString());
            }

            string extension = Path.GetExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return normalizedPath + ".unity";
            }

            if (!string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandFailureException("SCENE_PATH_INVALID", "scene 경로는 `.unity` 확장자를 사용해야 합니다: " + normalizedPath);
            }

            return normalizedPath;
        }

        private static string RequireExistingScenePath(string? path, string commandName)
        {
            string scenePath = ResolveScenePath(path);
            if (!AssetCommandSupport.AssetExists(scenePath))
            {
                throw new CommandFailureException("SCENE_PATH_INVALID", commandName + " 대상 scene이 없습니다: " + scenePath);
            }

            Type? mainType = AssetDatabase.GetMainAssetTypeAtPath(scenePath);
            if (mainType != typeof(SceneAsset))
            {
                throw new CommandFailureException("SCENE_PATH_INVALID", "scene asset이 아닙니다: " + scenePath);
            }

            return scenePath;
        }
    }

}
