#nullable enable
using System;

namespace UnityCli.Protocol
{
    [Serializable]
    public sealed class PingPayload
    {
        public string message = string.Empty;
        public string timestampUtc = string.Empty;
    }

    [Serializable]
    public sealed class StatusPayload
    {
        public string projectRoot = string.Empty;
        public string projectHash = string.Empty;
        public string projectName = string.Empty;
        public string unityVersion = string.Empty;
        public bool isPlaying;
        public bool isPaused;
        public bool isCompiling;
        public bool isUpdating;
        public string activeScenePath = string.Empty;
        public string pipeName = string.Empty;
    }

    [Serializable]
    public sealed class MessagePayload
    {
        public string message = string.Empty;
    }

    [Serializable]
    public sealed class PlayStatePayload
    {
        public bool isPlaying;
    }

    [Serializable]
    public sealed class PauseStatePayload
    {
        public bool isPaused;
    }

    [Serializable]
    public sealed class StopStatePayload
    {
        public bool isPlaying;
        public bool isPaused;
    }

    [Serializable]
    public sealed class ExecuteMenuArgs
    {
        public string path = string.Empty;
        public bool list;
        public string? prefix;
    }

    [Serializable]
    public sealed class ExecuteMenuPayload
    {
        public string path = string.Empty;
        public bool executed;
        public string? prefix;
        public string[] menus = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ScreenshotArgs
    {
        public string? view;
        public string? camera;
        public string? outputPath;
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class ScreenshotPayload
    {
        public string savedPath = string.Empty;
        public int width;
        public int height;
        public int screenWidth;
        public int screenHeight;
        public string coordinateOrigin = "bottom-left";
        public string imageOrigin = "top-left";
        public long fileSizeBytes;
    }

    [Serializable]
    public sealed class ExecuteCodeArgs
    {
        public string code = string.Empty;
        public string? argumentsJson;
        public bool force;
        public int timeoutMs;
    }

    [Serializable]
    public sealed class ExecuteCodePayload
    {
        public string output = string.Empty;
        public bool success;
        public string? error;
        public string? result;
        public bool hasResult;
    }

    [Serializable]
    public sealed class CustomCommandArgs
    {
        public string commandName = string.Empty;
        public string argumentsJson = "{}";
    }

    [Serializable]
    public sealed class CustomCommandPayload
    {
        public string commandName = string.Empty;
        public string resultJson = "{}";
    }

    [Serializable]
    public sealed class ReadConsoleArgs
    {
        public int limit = ProtocolConstants.DefaultConsoleLimit;
        public string? type;
    }

    [Serializable]
    public sealed class ReadConsolePayload
    {
        public ConsoleLogEntry[] entries = Array.Empty<ConsoleLogEntry>();
    }

    [Serializable]
    public sealed class PackageAddArgs
    {
        public string name = string.Empty;
        public string? version;
    }

    [Serializable]
    public sealed class PackageRemoveArgs
    {
        public string name = string.Empty;
        public bool force;
    }

    [Serializable]
    public sealed class PackageSearchArgs
    {
        public string query = string.Empty;
    }

    [Serializable]
    public sealed class PackageRecord
    {
        public string name = string.Empty;
        public string version = string.Empty;
        public string displayName = string.Empty;
        public string source = string.Empty;
    }

    [Serializable]
    public sealed class PackageListPayload
    {
        public PackageRecord[] packages = Array.Empty<PackageRecord>();
    }

    [Serializable]
    public sealed class PackageMutationPayload
    {
        public string name = string.Empty;
        public string version = string.Empty;
        public bool added;
        public bool removed;
    }

    [Serializable]
    public sealed class PackageSearchPayload
    {
        public PackageRecord[] results = Array.Empty<PackageRecord>();
    }

    [Serializable]
    public sealed class MaterialInfoArgs
    {
        public string path = string.Empty;
        public bool omitDefaults;
    }

    [Serializable]
    public sealed class MaterialSetArgs
    {
        public string path = string.Empty;
        public string? property;
        public string? value;
        public string? texture;
        public string? textureAsset;
    }

    [Serializable]
    public sealed class MaterialPropertyRecord
    {
        public string name = string.Empty;
        public string type = string.Empty;
        public string value = string.Empty;
    }

    [Serializable]
    public sealed class MaterialInfoPayload
    {
        public string path = string.Empty;
        public string shader = string.Empty;
        public MaterialPropertyRecord[] properties = Array.Empty<MaterialPropertyRecord>();
    }

    [Serializable]
    public sealed class MaterialSetPayload
    {
        public string path = string.Empty;
        public string property = string.Empty;
        public string previousValue = string.Empty;
        public string newValue = string.Empty;
    }

    [Serializable]
    public sealed class AssetFindArgs
    {
        public string name = string.Empty;
        public string? type;
        public string? folder;
        public int limit = ProtocolConstants.DefaultAssetFindLimit;
    }

    [Serializable]
    public sealed class AssetInfoArgs
    {
        public string? path;
        public string? guid;
    }

    [Serializable]
    public sealed class AssetTypesPayload
    {
        public AssetCreateTypeDescriptor[] types = Array.Empty<AssetCreateTypeDescriptor>();
    }

    [Serializable]
    public sealed class AssetPathArgs
    {
        public string path = string.Empty;
        public bool force;
    }

    [Serializable]
    public sealed class AssetMoveArgs
    {
        public string from = string.Empty;
        public string to = string.Empty;
        public bool force;
    }

    [Serializable]
    public sealed class AssetRenameArgs
    {
        public string path = string.Empty;
        public string name = string.Empty;
        public bool force;
    }

    [Serializable]
    public sealed class AssetCreateArgs
    {
        public string type = string.Empty;
        public string path = string.Empty;
        public bool force;
        public string? script;
        public string? typeName;
        public string? dataJson;
        public string? optionsJson;
    }

    [Serializable]
    public sealed class SceneVector3Value
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class SceneOpenArgs
    {
        public string path = string.Empty;
        public bool force;
    }

    [Serializable]
    public sealed class SceneInspectArgs
    {
        public string path = string.Empty;
        public bool withValues;
        public int? maxDepth;
        public bool omitDefaults;
    }

    [Serializable]
    public sealed class ScenePatchArgs
    {
        public string path = string.Empty;
        public bool force;
        public string? parent;
        public string? primitive;
        public SceneVector3Value? position;
        public string specJson = string.Empty;
    }

    [Serializable]
    public sealed class SceneSetTransformArgs
    {
        public string node = string.Empty;
        public SceneVector3Value? position;
        public SceneVector3Value? rotation;
        public SceneVector3Value? scale;
    }

    [Serializable]
    public sealed class SceneAssignMaterialArgs
    {
        public string node = string.Empty;
        public string material = string.Empty;
    }

    [Serializable]
    public sealed class PrefabInspectArgs
    {
        public string path = string.Empty;
        public bool withValues;
        public int? maxDepth;
        public bool omitDefaults;
    }

    [Serializable]
    public sealed class PrefabCreateArgs
    {
        public string path = string.Empty;
        public bool force;
        public string specJson = string.Empty;
    }

    [Serializable]
    public sealed class PrefabPatchArgs
    {
        public string path = string.Empty;
        public bool force;
        public string specJson = string.Empty;
    }

    [Serializable]
    public sealed class AssetCreateTypeDescriptor
    {
        public string typeId = string.Empty;
        public string displayName = string.Empty;
        public string defaultExtension = string.Empty;
        public string origin = string.Empty;
        public bool supportsDataPatch;
        public string[] requiredOptions = Array.Empty<string>();
        public string[] optionalOptions = Array.Empty<string>();
        public string[] aliases = Array.Empty<string>();
        public string[] notes = Array.Empty<string>();
    }

    [Serializable]
    public sealed class AssetRecord
    {
        public string path = string.Empty;
        public string guid = string.Empty;
        public string assetName = string.Empty;
        public string mainType = string.Empty;
        public bool isFolder;
        public bool exists;
    }

    [Serializable]
    public sealed class AssetFindPayload
    {
        public AssetRecord[] results = Array.Empty<AssetRecord>();
    }

    [Serializable]
    public sealed class AssetMutationPayload
    {
        public AssetRecord asset = new AssetRecord();
        public bool created;
        public bool deleted;
        public bool reimported;
        public bool overwritten;
        public string previousPath = string.Empty;
    }

    [Serializable]
    public sealed class AssetCreatePayload
    {
        public AssetRecord asset = new AssetRecord();
        public string createdType = string.Empty;
        public bool overwritten;
    }

    [Serializable]
    public sealed class SceneOpenPayload
    {
        public AssetRecord asset = new AssetRecord();
        public string activeScenePath = string.Empty;
        public bool opened;
    }

    [Serializable]
    public sealed class SceneMutationPayload
    {
        public AssetRecord asset = new AssetRecord();
        public string activeScenePath = string.Empty;
        public bool patched;
        public string? createdPath;
        public string[]? warnings;
    }

    [Serializable]
    public sealed class SceneAssignMaterialPayload
    {
        public AssetRecord asset = new AssetRecord();
        public string activeScenePath = string.Empty;
        public string node = string.Empty;
        public string material = string.Empty;
        public string? previousMaterial;
    }

    [Serializable]
    public sealed class SceneSetTransformPayload
    {
        public AssetRecord asset = new AssetRecord();
        public string activeScenePath = string.Empty;
        public string node = string.Empty;
        public SceneVector3Value? position;
        public SceneVector3Value? rotation;
        public SceneVector3Value? scale;
    }

    [Serializable]
    public sealed class PrefabMutationPayload
    {
        public AssetRecord asset = new AssetRecord();
        public bool created;
        public bool patched;
        public bool overwritten;
        public string[]? warnings;
    }

    [Serializable]
    public sealed class ConsoleLogEntry
    {
        public string timestampUtc = string.Empty;
        public string type = string.Empty;
        public string message = string.Empty;
        public string stackTrace = string.Empty;
    }

    [Serializable]
    public sealed class QaClickArgs
    {
        public string? qaId;
        public string? target;
    }

    [Serializable]
    public sealed class QaClickPayload
    {
        public bool targetFound;
        public string resolvedPath = string.Empty;
        public string? qaId;
    }

    [Serializable]
    public sealed class QaTapArgs
    {
        public int x;
        public int y;
        public int screenshotWidth;
        public int screenshotHeight;
    }

    [Serializable]
    public sealed class QaTapPayload
    {
        public bool completed;
    }

    [Serializable]
    public sealed class QaSwipeArgs
    {
        public string target = string.Empty;
        public int fromX;
        public int fromY;
        public int toX;
        public int toY;
        public int durationMs = ProtocolConstants.DefaultQaSwipeDurationMs;
        public int screenshotWidth;
        public int screenshotHeight;
    }

    [Serializable]
    public sealed class QaSwipePayload
    {
        public bool completed;
    }

    [Serializable]
    public sealed class QaKeyArgs
    {
        public string key = string.Empty;
    }

    [Serializable]
    public sealed class QaKeyPayload
    {
        public bool completed;
    }

    [Serializable]
    public sealed class QaWaitUntilArgs
    {
        public string? scene;
        public string? logContains;
        public string? objectExists;
        public int timeoutMs = ProtocolConstants.DefaultQaWaitUntilTimeoutMs;
    }

    [Serializable]
    public sealed class QaWaitUntilPayload
    {
        public bool conditionMet;
        public int elapsedMs;
        public string? reason;
    }

    [Serializable]
    public sealed class TestListArgs
    {
        public string mode = "all";
    }

    [Serializable]
    public sealed class TestListPayload
    {
        public string mode = string.Empty;
        public TestListEntry[] tests = Array.Empty<TestListEntry>();
    }

    [Serializable]
    public sealed class TestListEntry
    {
        public string fullName = string.Empty;
        public string assembly = string.Empty;
        public string mode = string.Empty;
        public string[] categories = Array.Empty<string>();
    }

    [Serializable]
    public sealed class TestRunArgs
    {
        public string mode = string.Empty;
        public string filter = string.Empty;
        public string category = string.Empty;
        public string assembly = string.Empty;
        public bool noDomainReload;
        public int timeoutSeconds;
    }

    [Serializable]
    public sealed class TestRunStartedPayload
    {
        public string runId = string.Empty;
        public string mode = string.Empty;
        public string status = "STARTED";
        public string startedAt = string.Empty;
    }

    [Serializable]
    public sealed class TestResultsArgs
    {
        public string runId = string.Empty;
    }

    [Serializable]
    public sealed class TestRunResultPayload
    {
        public string runId = string.Empty;
        public string mode = string.Empty;
        public string status = string.Empty;
        public string startedAt = string.Empty;
        public long durationMs;
        public TestRunSummary summary = new TestRunSummary();
        public TestResultEntry[] tests = Array.Empty<TestResultEntry>();
        public string[] warnings = Array.Empty<string>();
    }

    [Serializable]
    public sealed class TestRunSummary
    {
        public int total;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public int completed;
    }

    [Serializable]
    public sealed class TestResultEntry
    {
        public string fullName = string.Empty;
        public string assembly = string.Empty;
        public string[] categories = Array.Empty<string>();
        public string outcome = string.Empty;
        public long durationMs;
        public string message = string.Empty;
        public string stackTrace = string.Empty;
    }
}
