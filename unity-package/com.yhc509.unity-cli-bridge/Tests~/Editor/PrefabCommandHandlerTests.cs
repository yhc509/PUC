#nullable enable
using NUnit.Framework;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class PrefabCommandHandlerTests
    {
        private const string TestFolder = "Assets/UnityCliBridgePrefabCommandHandlerTests";

        private SceneSetup[]? _previousSceneSetup;

        [SetUp]
        public void SetUp()
        {
            _previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            CloseCurrentPrefabStage(saveIfDirty: true);
            EnsureTestFolder();
        }

        [TearDown]
        public void TearDown()
        {
            CloseCurrentPrefabStage(saveIfDirty: true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            if (_previousSceneSetup != null && _previousSceneSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(_previousSceneSetup);
            }
        }

        [Test]
        public void Patch_WhenTargetPrefabStageIsDirty_RefusesBeforeMutation()
        {
            string prefabPath = CreatePrefabAsset("DirtyPatchTarget", "DirtyPatchTarget");
            PrefabStage stage = OpenDirtyPrefabStage(prefabPath);

            CommandFailureException failure = Assert.Throws<CommandFailureException>(() =>
                PatchAddChild(prefabPath, "DirtyPatchTarget", "CliChild"))!;

            AssertPrefabStageDirtyFailure(failure, prefabPath);
            Assert.That(stage.scene.isDirty, Is.True);
            AssertPrefabDoesNotHaveChild(prefabPath, "CliChild");
        }

        [Test]
        public void CreateOverwrite_WhenTargetPrefabStageIsDirty_RefusesEvenWithForce()
        {
            string prefabPath = CreatePrefabAsset("DirtyCreateTarget", "DirtyCreateTarget");
            OpenDirtyPrefabStage(prefabPath);

            var args = new PrefabCreateArgs
            {
                path = prefabPath,
                force = true,
                specJson = "{\"version\":1,\"root\":{\"name\":\"ReplacementRoot\"}}",
            };

            CommandFailureException failure = Assert.Throws<CommandFailureException>(() =>
                new PrefabCommandHandler().Handle(ProtocolConstants.CommandPrefabCreate, ProtocolJson.Serialize(args)))!;

            AssertPrefabStageDirtyFailure(failure, prefabPath);
            AssertPrefabRootName(prefabPath, "DirtyCreateTarget");
        }

        [Test]
        public void Patch_WhenNoPrefabStage_AllowsMutation()
        {
            string prefabPath = CreatePrefabAsset("NoStageTarget", "NoStageTarget");
            CloseCurrentPrefabStage(saveIfDirty: true);

            PatchAddChild(prefabPath, "NoStageTarget", "CliChild");

            AssertPrefabHasChild(prefabPath, "CliChild");
        }

        [Test]
        public void Patch_WhenDifferentPrefabStageIsDirty_AllowsMutation()
        {
            string targetPath = CreatePrefabAsset("OtherStageTarget", "OtherStageTarget");
            string otherPath = CreatePrefabAsset("OtherStageOpen", "OtherStageOpen");
            PrefabStage otherStage = OpenDirtyPrefabStage(otherPath);

            PatchAddChild(targetPath, "OtherStageTarget", "CliChild");

            Assert.That(otherStage.scene.isDirty, Is.True);
            AssertPrefabHasChild(targetPath, "CliChild");
        }

        [Test]
        public void Patch_WhenTargetPrefabStageIsClean_AllowsMutation()
        {
            string prefabPath = CreatePrefabAsset("CleanStageTarget", "CleanStageTarget");
            PrefabStage stage = PrefabStageUtility.OpenPrefab(prefabPath);
            Assert.That(stage.scene.isDirty, Is.False);

            PatchAddChild(prefabPath, "CleanStageTarget", "CliChild");

            AssertPrefabHasChild(prefabPath, "CliChild");
        }

        private static void PatchAddChild(string prefabPath, string rootName, string childName)
        {
            var args = new PrefabPatchArgs
            {
                path = prefabPath,
                specJson = "{\"version\":1,\"operations\":[{\"op\":\"add-child\",\"parent\":\"/"
                    + rootName
                    + "[0]\",\"node\":{\"name\":\""
                    + childName
                    + "\"}}]}",
            };

            new PrefabCommandHandler().Handle(ProtocolConstants.CommandPrefabPatch, ProtocolJson.Serialize(args));
        }

        private static PrefabStage OpenDirtyPrefabStage(string prefabPath)
        {
            PrefabStage stage = PrefabStageUtility.OpenPrefab(prefabPath);
            var child = new GameObject("UnsavedStageChild");
            child.transform.SetParent(stage.prefabContentsRoot.transform, false);
            EditorUtility.SetDirty(stage.prefabContentsRoot);
            EditorSceneManager.MarkSceneDirty(stage.scene);
            Assert.That(stage.scene.isDirty, Is.True);
            return stage;
        }

        private static string CreatePrefabAsset(string fileName, string rootName)
        {
            EnsureTestFolder();
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(TestFolder + "/" + fileName + ".prefab");
            var root = new GameObject(rootName);
            try
            {
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Assert.That(saved, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return prefabPath;
        }

        private static void AssertPrefabStageDirtyFailure(CommandFailureException failure, string prefabPath)
        {
            Assert.That(failure.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorPrefabStageDirty));
            Assert.That(failure.Message, Does.Contain(prefabPath));
            Assert.That(failure.Message, Does.Contain("Prefab Stage"));
            Assert.That(failure.Message, Does.Contain("저장하지 않은 변경"));
        }

        private static void AssertPrefabHasChild(string prefabPath, string childName)
        {
            Assert.That(PrefabHasChild(prefabPath, childName), Is.True);
        }

        private static void AssertPrefabDoesNotHaveChild(string prefabPath, string childName)
        {
            Assert.That(PrefabHasChild(prefabPath, childName), Is.False);
        }

        private static bool PrefabHasChild(string prefabPath, string childName)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                return root.transform.Find(childName) != null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertPrefabRootName(string prefabPath, string rootName)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Assert.That(root.name, Is.EqualTo(rootName));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureTestFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder("Assets", "UnityCliBridgePrefabCommandHandlerTests");
            }
        }

        private static void CloseCurrentPrefabStage(bool saveIfDirty)
        {
            PrefabStage? stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return;
            }

            if (saveIfDirty && stage.scene.isDirty)
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                stage.ClearDirtiness();
            }

            StageUtility.GoToMainStage();
        }
    }
}
