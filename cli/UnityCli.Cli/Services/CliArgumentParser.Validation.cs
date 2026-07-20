#nullable enable
using System.Linq;
using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

public static partial class CliArgumentParser
{
    private static string RequireScreenshotView(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "game" => "game",
            "scene" => "scene",
            _ => throw new CliUsageException("`--view`는 `game` 또는 `scene`만 지원합니다."),
        };
    }

    private static string RequireScreenshotFormat(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "png" => "png",
            "jpg" => "jpg",
            "jpeg" => "jpeg",
            _ => throw new CliUsageException("`--format`은 `png`, `jpg`, `jpeg` 중 하나여야 합니다."),
        };
    }

    private static int RequireScreenshotQuality(string value)
    {
        int quality = RequireInt(value, "--quality");
        if (quality > 100)
        {
            throw new CliUsageException("--quality 값은 1 이상 100 이하의 정수여야 합니다.");
        }

        return quality;
    }

    private static int RequireInt(string value, string option, int? minimumValue = 1)
    {
        if (!int.TryParse(value, out var result))
        {
            throw new CliUsageException($"{option} 값은 {DescribeIntegerRequirement(minimumValue)}여야 합니다.");
        }

        if (minimumValue.HasValue && result < minimumValue.Value)
        {
            throw new CliUsageException($"{option} 값은 {DescribeIntegerRequirement(minimumValue)}여야 합니다.");
        }

        return result;
    }

    private static string DescribeIntegerRequirement(int? minimumValue)
    {
        return minimumValue switch
        {
            null => "정수",
            0 => "0 이상의 정수",
            1 => "1 이상의 정수",
            _ => minimumValue.Value + " 이상의 정수",
        };
    }

    private static string RequireValue(Queue<string> tokens, string option)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException($"{option} 값이 비어 있습니다.");
        }

        return tokens.Dequeue();
    }

    private static string RequireJsonValue(Queue<string> tokens, string option)
    {
        if (tokens.Count == 0 || tokens.Peek().StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"{option} 옵션은 JSON 값이 필요합니다.");
        }

        string value = tokens.Dequeue();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"{option} 옵션은 JSON 값이 필요합니다.");
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException exception)
        {
            throw new CliUsageException($"{option} 값은 올바른 JSON이어야 합니다. " + exception.Message);
        }
    }

    private static string RequireAssetPath(string value, string option, bool allowPackages = false)
    {
        try
        {
            return AssetPathUtility.Normalize(value, allowPackages);
        }
        catch (InvalidOperationException)
        {
            throw new CliUsageException(
                allowPackages
                    ? $"{option} 값은 `Assets/...` 또는 `Packages/...` 형식이어야 합니다."
                    : $"{option} 값은 `Assets/...` 형식이어야 합니다.");
        }
    }

    private static string RequireAssetCreateType(string value)
    {
        string normalized = BuiltInAssetCreateCatalog.NormalizeTypeId(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new CliUsageException("`asset create --type` 값이 비어 있습니다.");
        }

        return normalized;
    }

    private static string RequireScenePrimitive(string value)
    {
        string normalized = ProtocolConstants.NormalizeScenePrimitive(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new CliUsageException("`--primitive`는 `" + string.Join("`, `", ProtocolConstants.SupportedScenePrimitiveNames) + "` 중 하나여야 합니다.");
        }

        return normalized;
    }

    private static string RequireTestMode(string value, bool allowAll)
    {
        string normalized = value.ToLowerInvariant();
        if (normalized is "edit" or "play" || (allowAll && normalized == "all"))
        {
            return normalized;
        }

        string allowed = allowAll ? "edit, play, all" : "edit, play";
        throw new CliUsageException($"--mode 값은 {allowed} 중 하나여야 합니다.");
    }

    private static string RequireQaButton(string value)
    {
        string normalized = value.ToLowerInvariant();
        if (normalized is "left" or "right")
        {
            return normalized;
        }

        throw new CliUsageException("`--button` 값은 `left` 또는 `right` 중 하나여야 합니다.");
    }

    private static string RequireSubcommand(Queue<string> tokens, string command)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException($"`{command}` 다음에는 하위 명령이 필요합니다.");
        }

        return tokens.Dequeue().ToLowerInvariant();
    }

    private static void ValidateAssetOptions(ParsedCommand parsed)
    {
        switch (parsed.Kind)
        {
            case CommandKind.AssetFind
                when string.IsNullOrWhiteSpace(parsed.AssetName)
                && string.IsNullOrWhiteSpace(parsed.AssetType):
                throw new CliUsageException("`asset find`에는 `--name` 또는 `--type` 중 하나 이상이 필요합니다.");
            case CommandKind.AssetInfo:
            {
                var hasPath = !string.IsNullOrWhiteSpace(parsed.AssetPath);
                var hasGuid = !string.IsNullOrWhiteSpace(parsed.AssetGuid);
                if (hasPath == hasGuid)
                {
                    throw new CliUsageException("`asset info`에는 `--path` 또는 `--guid` 중 하나만 필요합니다.");
                }

                break;
            }
            case CommandKind.AssetReimport when string.IsNullOrWhiteSpace(parsed.AssetPath):
                throw new CliUsageException("`asset reimport`에는 `--path`가 필요합니다.");
            case CommandKind.AssetMkdir when string.IsNullOrWhiteSpace(parsed.AssetPath):
                throw new CliUsageException("`asset mkdir`에는 `--path`가 필요합니다.");
            case CommandKind.AssetMove when string.IsNullOrWhiteSpace(parsed.AssetFrom) || string.IsNullOrWhiteSpace(parsed.AssetTo):
                throw new CliUsageException("`asset move`에는 `--from`과 `--to`가 모두 필요합니다.");
            case CommandKind.AssetRename when string.IsNullOrWhiteSpace(parsed.AssetPath) || string.IsNullOrWhiteSpace(parsed.AssetNewName):
                throw new CliUsageException("`asset rename`에는 `--path`와 `--name`이 모두 필요합니다.");
            case CommandKind.AssetDelete when string.IsNullOrWhiteSpace(parsed.AssetPath):
                throw new CliUsageException("`asset delete`에는 `--path`가 필요합니다.");
            case CommandKind.AssetDelete when ForceRequiredByCatalog(parsed):
                throw new CliUsageException("`asset delete`는 `--force`가 필요합니다.");
            case CommandKind.AssetCreate when string.IsNullOrWhiteSpace(parsed.AssetCreateType) || string.IsNullOrWhiteSpace(parsed.AssetPath):
                throw new CliUsageException("`asset create`에는 `--type`과 `--path`가 필요합니다.");
            case CommandKind.AssetCreate when parsed.AssetCreateType == "scriptable-object"
                && string.IsNullOrWhiteSpace(parsed.AssetScript)
                && string.IsNullOrWhiteSpace(parsed.AssetTypeName):
                throw new CliUsageException("`asset create --type scriptable-object`에는 `--script` 또는 `--type-name`이 필요합니다.");
            case CommandKind.AssetCreate when parsed.AssetCreateType == "animator-override-controller"
                && string.IsNullOrWhiteSpace(parsed.AssetBaseController):
                throw new CliUsageException("`asset create --type animator-override-controller`에는 `--base-controller`가 필요합니다.");
            case CommandKind.AssetCreate when IsBuiltInAssetCreateType(parsed.AssetCreateType)
                && parsed.AssetCustomOptions.Count > 0:
                throw new CliUsageException(
                    "`asset create --type " + parsed.AssetCreateType + "`에서 지원하지 않는 옵션입니다: "
                    + string.Join(", ", parsed.AssetCustomOptions.Keys.Select(key => "--" + ToKebabCase(key))));
            case CommandKind.PrefabInspect when string.IsNullOrWhiteSpace(parsed.PrefabPath):
                throw new CliUsageException("`prefab inspect`에는 `--path`가 필요합니다.");
            case CommandKind.PrefabCreate when string.IsNullOrWhiteSpace(parsed.PrefabPath):
                throw new CliUsageException("`prefab create`에는 `--path`가 필요합니다.");
            case CommandKind.PrefabPatch when string.IsNullOrWhiteSpace(parsed.PrefabPath):
                throw new CliUsageException("`prefab patch`에는 `--path`가 필요합니다.");
            case CommandKind.PrefabAddComponent when string.IsNullOrWhiteSpace(parsed.PrefabPath):
                throw new CliUsageException("`prefab add-component`에는 `--path`가 필요합니다.");
            case CommandKind.PrefabAddComponent when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`prefab add-component`에는 `--node`가 필요합니다.");
            case CommandKind.PrefabAddComponent when string.IsNullOrWhiteSpace(parsed.SceneComponentType):
                throw new CliUsageException("`prefab add-component`에는 `--type`이 필요합니다.");
            case CommandKind.PrefabRemoveComponent when string.IsNullOrWhiteSpace(parsed.PrefabPath):
                throw new CliUsageException("`prefab remove-component`에는 `--path`가 필요합니다.");
            case CommandKind.PrefabRemoveComponent when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`prefab remove-component`에는 `--node`가 필요합니다.");
            case CommandKind.PrefabRemoveComponent when string.IsNullOrWhiteSpace(parsed.SceneComponentType):
                throw new CliUsageException("`prefab remove-component`에는 `--type`이 필요합니다.");
            case CommandKind.PrefabRemoveComponent when ForceRequiredByCatalog(parsed):
                throw new CliUsageException("`prefab remove-component`에는 `--force`가 필요합니다.");
            case CommandKind.PrefabListComponents when string.IsNullOrWhiteSpace(parsed.PrefabPath):
                throw new CliUsageException("`prefab list-components`에는 `--path`가 필요합니다.");
            case CommandKind.PrefabListComponents when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`prefab list-components`에는 `--node`가 필요합니다.");
            case CommandKind.PrefabCreate when HasInvalidPrefabSpecSource(parsed):
                throw new CliUsageException("`prefab create`에는 `--spec-file` 또는 `--spec-json` 중 하나만 필요합니다.");
            case CommandKind.PrefabPatch when HasInvalidPrefabSpecSource(parsed):
                throw new CliUsageException("`prefab patch`에는 `--spec-file` 또는 `--spec-json` 중 하나만 필요합니다.");
            case CommandKind.PrefabPatch when ForceRequiredByCatalog(parsed):
                throw new CliUsageException("`prefab patch`에서 destructive operation을 쓰려면 `--force`가 필요합니다.");
        }
    }

    private static void ValidateSceneOptions(ParsedCommand parsed)
    {
        switch (parsed.Kind)
        {
            case CommandKind.SceneOpen when string.IsNullOrWhiteSpace(parsed.ScenePath):
                throw new CliUsageException("`scene open`에는 `--path`가 필요합니다.");
            case CommandKind.SceneInspect when string.IsNullOrWhiteSpace(parsed.ScenePath):
                throw new CliUsageException("`scene inspect`에는 `--path`가 필요합니다.");
            case CommandKind.ScenePatch when string.IsNullOrWhiteSpace(parsed.ScenePath):
                throw new CliUsageException("`scene patch`에는 `--path`가 필요합니다.");
            case CommandKind.ScenePatch when HasInvalidSceneSpecSource(parsed):
                throw new CliUsageException("`scene patch`에는 `--spec-file` 또는 `--spec-json` 중 하나만 필요합니다.");
            case CommandKind.ScenePatch when ForceRequiredByCatalog(parsed):
                throw new CliUsageException("`scene patch`에서 `delete-gameobject` 또는 `remove-component`를 쓰려면 `--force`가 필요합니다.");
            case CommandKind.SceneAddObject when string.IsNullOrWhiteSpace(parsed.ScenePath):
                throw new CliUsageException("`scene add-object`에는 `--path`가 필요합니다.");
            case CommandKind.SceneAddObject when string.IsNullOrWhiteSpace(parsed.SceneObjectName):
                throw new CliUsageException("`scene add-object`에는 `--name`이 필요합니다.");
            case CommandKind.SceneSetTransform when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`scene set-transform`에는 `--node`가 필요합니다.");
            case CommandKind.SceneSetTransform
                when string.IsNullOrWhiteSpace(parsed.ScenePosition)
                && string.IsNullOrWhiteSpace(parsed.SceneRotation)
                && string.IsNullOrWhiteSpace(parsed.SceneScale):
                throw new CliUsageException("`scene set-transform`에는 `--position`, `--rotation`, `--scale` 중 하나 이상이 필요합니다.");
            case CommandKind.SceneAddComponent when string.IsNullOrWhiteSpace(parsed.ScenePath):
                throw new CliUsageException("`scene add-component`에는 `--path`가 필요합니다.");
            case CommandKind.SceneAddComponent when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`scene add-component`에는 `--node`가 필요합니다.");
            case CommandKind.SceneAddComponent when string.IsNullOrWhiteSpace(parsed.SceneComponentType):
                throw new CliUsageException("`scene add-component`에는 `--type`이 필요합니다.");
            case CommandKind.SceneRemoveComponent when string.IsNullOrWhiteSpace(parsed.ScenePath):
                throw new CliUsageException("`scene remove-component`에는 `--path`가 필요합니다.");
            case CommandKind.SceneRemoveComponent when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`scene remove-component`에는 `--node`가 필요합니다.");
            case CommandKind.SceneRemoveComponent when string.IsNullOrWhiteSpace(parsed.SceneComponentType):
                throw new CliUsageException("`scene remove-component`에는 `--type`이 필요합니다.");
            case CommandKind.SceneRemoveComponent when ForceRequiredByCatalog(parsed):
                throw new CliUsageException("`scene remove-component`는 `--force`가 필요합니다.");
            case CommandKind.SceneListComponents when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`scene list-components`에는 `--node`가 필요합니다.");
            case CommandKind.SceneAssignMaterial when string.IsNullOrWhiteSpace(parsed.SceneTarget):
                throw new CliUsageException("`scene assign-material`에는 `--node`가 필요합니다.");
            case CommandKind.SceneAssignMaterial when string.IsNullOrWhiteSpace(parsed.MaterialPath):
                throw new CliUsageException("`scene assign-material`에는 `--material`이 필요합니다.");
        }
    }

    private static void ValidateScreenshotOptions(ParsedCommand parsed)
    {
        if (parsed.Kind != CommandKind.Screenshot)
        {
            return;
        }

        bool hasView = !string.IsNullOrWhiteSpace(parsed.ScreenshotView);
        bool hasCamera = !string.IsNullOrWhiteSpace(parsed.ScreenshotCamera);

        if (hasView && hasCamera)
        {
            throw new CliUsageException("`screenshot`에는 `--view` 또는 `--camera` 중 하나만 필요합니다.");
        }

        if (!hasView && !hasCamera)
        {
            parsed.ScreenshotView = ParsedCommand.DefaultScreenshotView;
        }
    }

    private static void ValidateExecuteMenuOptions(ParsedCommand parsed)
    {
        if (parsed.Kind != CommandKind.ExecuteMenu)
        {
            return;
        }

        bool hasPath = !string.IsNullOrWhiteSpace(parsed.MenuPath);
        bool hasList = parsed.MenuList;
        if (hasPath == hasList)
        {
            throw new CliUsageException("`execute-menu`에는 `--path` 또는 `--list <prefix>` 중 하나만 필요합니다.");
        }

        if (parsed.MenuList && string.IsNullOrWhiteSpace(parsed.MenuListPrefix))
        {
            throw new CliUsageException("`execute-menu --list`에는 prefix가 필요합니다.");
        }
    }

    private static void ValidatePackageOptions(ParsedCommand parsed)
    {
        switch (parsed.Kind)
        {
            case CommandKind.PackageAdd when string.IsNullOrWhiteSpace(parsed.PackageName):
                throw new CliUsageException("`package add`에는 `--name`이 필요합니다.");
            case CommandKind.PackageRemove when string.IsNullOrWhiteSpace(parsed.PackageName):
                throw new CliUsageException("`package remove`에는 `--name`이 필요합니다.");
            case CommandKind.PackageRemove when ForceRequiredByCatalog(parsed):
                throw new CliUsageException("`package remove`는 `--force`가 필요합니다.");
            case CommandKind.PackageSearch when string.IsNullOrWhiteSpace(parsed.PackageQuery):
                throw new CliUsageException("`package search`에는 `--query`가 필요합니다.");
        }
    }

    private static void ValidateTestOptions(ParsedCommand parsed)
    {
        switch (parsed.Kind)
        {
            case CommandKind.TestRun when string.IsNullOrWhiteSpace(parsed.TestMode):
                throw new CliUsageException("`test run`에는 `--mode <edit|play>`가 필요합니다.");
            case CommandKind.TestRun
                when string.Equals(parsed.TestMode, "edit", StringComparison.Ordinal)
                && parsed.TestNoDomainReload:
                throw new CliUsageException("--no-domain-reload는 --mode play에서만 사용 가능합니다.");
            case CommandKind.TestRun when parsed.TestTimeoutSeconds > ProtocolConstants.MaxTestRunTimeoutSeconds:
                throw new CliUsageException($"--timeout 값은 {ProtocolConstants.MaxTestRunTimeoutSeconds}초를 초과할 수 없습니다.");
            case CommandKind.TestResults
                when !string.IsNullOrEmpty(parsed.TestRunId)
                && !ProtocolHelpers.IsValid32HexId(parsed.TestRunId):
                throw new CliUsageException("--run-id 값은 32자리 16진수여야 합니다 (`test run`이 반환한 runId).");
        }
    }

    private static void ValidateRecordOptions(ParsedCommand parsed)
    {
        if (parsed.Kind == CommandKind.RecordStart && parsed.RecordWait && parsed.RecordDuration is null)
        {
            throw new CliUsageException("`record start --wait`는 `--duration <초>`와 함께 사용해야 합니다 (수동 녹화는 `record stop`으로 종료).");
        }

        if (!string.IsNullOrEmpty(parsed.RecordRunId) && !ProtocolHelpers.IsValid32HexId(parsed.RecordRunId))
        {
            throw new CliUsageException("--recording-id 값은 32자리 16진수여야 합니다 (`record start`가 반환한 recordingId).");
        }
    }

    private static void ValidateMaterialOptions(ParsedCommand parsed)
    {
        switch (parsed.Kind)
        {
            case CommandKind.MaterialInfo when string.IsNullOrWhiteSpace(parsed.MaterialPath):
                throw new CliUsageException("`material info`에는 `--path`가 필요합니다.");
            case CommandKind.MaterialSet when string.IsNullOrWhiteSpace(parsed.MaterialPath):
                throw new CliUsageException("`material set`에는 `--path`가 필요합니다.");
            case CommandKind.MaterialSet:
            {
                bool hasPropertySet = !string.IsNullOrWhiteSpace(parsed.MaterialProperty)
                    && !string.IsNullOrWhiteSpace(parsed.MaterialValue);
                bool hasTextureSet = !string.IsNullOrWhiteSpace(parsed.MaterialTexture)
                    && !string.IsNullOrWhiteSpace(parsed.MaterialTextureAsset);
                if (!hasPropertySet && !hasTextureSet)
                {
                    throw new CliUsageException("`material set`에는 `--property`+`--value` 또는 `--texture`+`--asset` 조합이 필요합니다.");
                }

                break;
            }
            case CommandKind.QaRunSequence:
                if (parsed.QaSequenceArgs == null || parsed.QaSequenceArgs.steps.Length == 0)
                {
                    throw new CliUsageException("`qa run-sequence`에는 `--spec-json <json|@file>`(비어 있지 않은 steps)이 필요합니다.");
                }

                if (parsed.QaSequenceTimeoutMs < 0
                    || parsed.QaSequenceTimeoutMs > ProtocolConstants.MaxQaRunSequenceTimeoutMs)
                {
                    throw new CliUsageException($"`qa run-sequence`의 `--timeout`은 0~{ProtocolConstants.MaxQaRunSequenceTimeoutMs}ms 범위여야 합니다.");
                }

                break;
        }
    }

    private static void ValidateQaOptions(ParsedCommand parsed)
    {
        if (parsed.Kind is CommandKind.QaClick or CommandKind.QaTap or CommandKind.QaSwipe)
        {
            parsed.QaButton = RequireQaButton(parsed.QaButton);
        }

        switch (parsed.Kind)
        {
            case CommandKind.QaClick:
            {
                bool hasQaId = !string.IsNullOrWhiteSpace(parsed.QaId);
                bool hasTarget = !string.IsNullOrWhiteSpace(parsed.QaTarget);
                if (hasQaId == hasTarget)
                {
                    throw new CliUsageException("`qa click`에는 `--qa-id` 또는 `--target` 중 하나만 필요합니다.");
                }

                break;
            }
            case CommandKind.QaTap:
            {
                bool hasTarget = !string.IsNullOrWhiteSpace(parsed.QaTarget);
                bool hasAnyCoord = parsed.QaTapX.HasValue || parsed.QaTapY.HasValue;
                if (hasTarget)
                {
                    if (hasAnyCoord)
                    {
                        throw new CliUsageException("`qa tap`에서 `--target`과 `--x`/`--y`는 함께 사용할 수 없습니다.");
                    }
                }
                else if (!parsed.QaTapX.HasValue || !parsed.QaTapY.HasValue)
                {
                    throw new CliUsageException("`qa tap`에는 `--x`와 `--y`가 모두 필요합니다(또는 `--target`).");
                }

                ValidateQaScreenshotDimensions(parsed, "`qa tap`");
                break;
            }
            case CommandKind.QaUiDump:
                ValidateQaScreenshotDimensions(parsed, "`qa ui-dump`");
                break;
            case CommandKind.QaWorldDump:
                ValidateQaScreenshotDimensions(parsed, "`qa world-dump`");
                break;
            case CommandKind.QaSwipe when string.IsNullOrWhiteSpace(parsed.QaSwipeFrom) || string.IsNullOrWhiteSpace(parsed.QaSwipeTo):
                throw new CliUsageException("`qa swipe`에는 `--from`과 `--to`가 모두 필요합니다.");
            case CommandKind.QaSwipe:
                bool usesTargetRelativeOffsets = !string.IsNullOrWhiteSpace(parsed.QaTarget);
                RequireQaSwipeCoordinatePair(parsed.QaSwipeFrom!, "--from", usesTargetRelativeOffsets);
                RequireQaSwipeCoordinatePair(parsed.QaSwipeTo!, "--to", usesTargetRelativeOffsets);
                ValidateQaScreenshotDimensions(parsed, "`qa swipe`");
                if (usesTargetRelativeOffsets && (parsed.QaScreenshotWidth.HasValue || parsed.QaScreenshotHeight.HasValue))
                {
                    throw new CliUsageException("`qa swipe`에서 `--target`과 `--screenshot-width`/`--screenshot-height`는 함께 사용할 수 없습니다.");
                }

                break;
            case CommandKind.QaKey when string.IsNullOrWhiteSpace(parsed.QaKeyName):
                throw new CliUsageException("`qa key`에는 `--key`가 필요합니다.");
            case CommandKind.QaWait when parsed.QaWaitMs <= 0:
                throw new CliUsageException("`qa wait`에는 `--ms`가 필요합니다.");
            case CommandKind.QaWaitUntil:
            {
                bool hasScene = !string.IsNullOrWhiteSpace(parsed.QaWaitScene);
                bool hasLogContains = !string.IsNullOrWhiteSpace(parsed.QaWaitLogContains);
                bool hasObjectExists = !string.IsNullOrWhiteSpace(parsed.QaWaitObjectExists) || !string.IsNullOrWhiteSpace(parsed.QaId);
                bool hasObjectInteractable = !string.IsNullOrWhiteSpace(parsed.QaWaitObjectInteractable);
                bool hasObjectGone = !string.IsNullOrWhiteSpace(parsed.QaWaitObjectGone);
                if (!hasScene && !hasLogContains && !hasObjectExists && !hasObjectInteractable && !hasObjectGone)
                {
                    throw new CliUsageException("`qa wait-until`에는 `--scene`, `--log-contains`, `--object-exists`, `--object-interactable`, `--object-gone`, `--qa-id` 중 하나 이상이 필요합니다.");
                }

                if (!string.IsNullOrWhiteSpace(parsed.QaId) && !string.IsNullOrWhiteSpace(parsed.QaWaitObjectExists))
                {
                    throw new CliUsageException("`qa wait-until`에서는 `--qa-id`와 `--object-exists`를 동시에 쓸 수 없습니다.");
                }

                if (hasObjectGone
                    && ((hasObjectExists && string.Equals(parsed.QaWaitObjectGone, parsed.QaWaitObjectExists, StringComparison.Ordinal))
                        || (hasObjectInteractable && string.Equals(parsed.QaWaitObjectGone, parsed.QaWaitObjectInteractable, StringComparison.Ordinal))))
                {
                    throw new CliUsageException("동일 대상에 `--object-gone`과 `--object-exists`/`--object-interactable`을 함께 지정할 수 없습니다 (조건이 AND로 결합되어 영원히 timeout됩니다).");
                }

                if (!string.IsNullOrWhiteSpace(parsed.QaId))
                {
                    parsed.QaWaitObjectExists = parsed.QaId;
                }

                break;
            }
        }
    }

    private static void ValidateQaScreenshotDimensions(ParsedCommand parsed, string commandName)
    {
        bool hasScreenshotWidth = parsed.QaScreenshotWidth.HasValue;
        bool hasScreenshotHeight = parsed.QaScreenshotHeight.HasValue;
        if (hasScreenshotWidth != hasScreenshotHeight)
        {
            throw new CliUsageException($"{commandName}에서 `--screenshot-width`와 `--screenshot-height`는 함께 지정해야 합니다.");
        }
    }

    private static void RequireQaSwipeCoordinatePair(string value, string option, bool usesTargetRelativeOffsets)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _))
        {
            throw new CliUsageException($"{option} 값은 {GetQaSwipeCoordinateDescription(usesTargetRelativeOffsets)}이어야 합니다.");
        }
    }

    private static string GetQaSwipeCoordinateDescription(bool usesTargetRelativeOffsets)
    {
        return usesTargetRelativeOffsets
            ? "`x,y` 형식의 target 중심 기준 픽셀 오프셋"
            : "`x,y` 형식의 절대 화면 픽셀 좌표";
    }

    private static void ValidateExecuteOptions(ParsedCommand parsed)
    {
        if (parsed.Kind != CommandKind.ExecuteCode)
        {
            return;
        }

        bool hasCode = !string.IsNullOrWhiteSpace(parsed.ExecuteCodeSnippet);
        bool hasFile = !string.IsNullOrWhiteSpace(parsed.ExecuteCodeFile);

        if (hasCode == hasFile)
        {
            throw new CliUsageException("`execute`에는 `--code` 또는 `--file` 중 하나만 필요합니다.");
        }

        if (ForceRequiredByCatalog(parsed))
        {
            throw new CliUsageException("`execute`는 `--force`가 필요합니다.");
        }

        if (parsed.ExecuteCodeTimeoutSeconds.HasValue)
        {
            int seconds = parsed.ExecuteCodeTimeoutSeconds.Value;
            int maxSeconds = ProtocolConstants.MaxExecuteTimeoutMs / 1000;
            if (seconds <= 0 || seconds > maxSeconds)
            {
                throw new CliUsageException(
                    $"--timeout 값은 1~{maxSeconds} 사이의 정수(초)여야 합니다.");
            }
        }
    }

    internal static bool ForceRequiredByCatalog(ParsedCommand parsed)
    {
        if (parsed.Force)
        {
            return false;
        }

        CliCommandDescriptor descriptor = GetCatalogDescriptor(parsed.Kind);
        return descriptor.ForceRule switch
        {
            ForceRule.None => false,
            ForceRule.Always => true,
            ForceRule.OnOverwrite => false,
            ForceRule.OnDestructiveOp => PatchContainsDestructiveOperation(parsed),
            _ => throw new InvalidOperationException("지원하지 않는 force rule입니다: " + descriptor.ForceRule),
        };
    }

    internal static CliCommandDescriptor GetCatalogDescriptor(CommandKind kind)
    {
        string commandPath = kind switch
        {
            CommandKind.Status => "status",
            CommandKind.Compile => "compile",
            CommandKind.Refresh => "refresh",
            CommandKind.ReadConsole => "read-console",
            CommandKind.Play => "play",
            CommandKind.Pause => "pause",
            CommandKind.Stop => "stop",
            CommandKind.ExecuteMenu => "execute-menu",
            CommandKind.Screenshot => "screenshot",
            CommandKind.ExecuteCode => "execute",
            CommandKind.Custom => "custom",
            CommandKind.AssetFind => "asset find",
            CommandKind.AssetTypes => "asset types",
            CommandKind.AssetInfo => "asset info",
            CommandKind.AssetReimport => "asset reimport",
            CommandKind.AssetMkdir => "asset mkdir",
            CommandKind.AssetMove => "asset move",
            CommandKind.AssetRename => "asset rename",
            CommandKind.AssetDelete => "asset delete",
            CommandKind.AssetCreate => "asset create",
            CommandKind.SceneOpen => "scene open",
            CommandKind.SceneInspect => "scene inspect",
            CommandKind.ScenePatch => "scene patch",
            CommandKind.SceneAddObject => "scene add-object",
            CommandKind.SceneSetTransform => "scene set-transform",
            CommandKind.SceneAddComponent => "scene add-component",
            CommandKind.SceneRemoveComponent => "scene remove-component",
            CommandKind.SceneListComponents => "scene list-components",
            CommandKind.SceneAssignMaterial => "scene assign-material",
            CommandKind.PrefabInspect => "prefab inspect",
            CommandKind.PrefabCreate => "prefab create",
            CommandKind.PrefabPatch => "prefab patch",
            CommandKind.PrefabAddComponent => "prefab add-component",
            CommandKind.PrefabRemoveComponent => "prefab remove-component",
            CommandKind.PrefabListComponents => "prefab list-components",
            CommandKind.TestList => "test list",
            CommandKind.TestRun => "test run",
            CommandKind.TestResults => "test results",
            CommandKind.TestCancel => "test cancel",
            CommandKind.RecordStart => "record start",
            CommandKind.RecordStop => "record stop",
            CommandKind.RecordStatus => "record status",
            CommandKind.PackageList => "package list",
            CommandKind.PackageAdd => "package add",
            CommandKind.PackageRemove => "package remove",
            CommandKind.PackageSearch => "package search",
            CommandKind.MaterialInfo => "material info",
            CommandKind.MaterialSet => "material set",
            CommandKind.QaClick => "qa click",
            CommandKind.QaTap => "qa tap",
            CommandKind.QaSwipe => "qa swipe",
            CommandKind.QaKey => "qa key",
            CommandKind.QaUiDump => "qa ui-dump",
            CommandKind.QaWorldDump => "qa world-dump",
            CommandKind.QaWait => "qa wait",
            CommandKind.QaWaitUntil => "qa wait-until",
            CommandKind.QaRunSequence => "qa run-sequence",
            CommandKind.InstancesList => "instances list",
            CommandKind.InstancesUse => "instances use",
            CommandKind.Doctor => "doctor",
            CommandKind.Raw => "raw",
            _ => throw new InvalidOperationException("catalog descriptor가 없는 command kind입니다: " + kind),
        };

        return CliCommandCatalog.FindByCommand(commandPath)
            ?? throw new InvalidOperationException("catalog descriptor를 찾지 못했습니다: " + commandPath);
    }

    private static bool PatchContainsDestructiveOperation(ParsedCommand parsed)
    {
        return parsed.Kind switch
        {
            CommandKind.ScenePatch => ScenePatchContainsDestructiveOperation(parsed),
            CommandKind.PrefabPatch => PrefabPatchContainsDestructiveOperation(parsed),
            _ => false,
        };
    }

    private static bool HasInvalidPrefabSpecSource(ParsedCommand parsed)
    {
        bool hasFile = !string.IsNullOrWhiteSpace(parsed.PrefabSpecFile);
        bool hasInline = !string.IsNullOrWhiteSpace(parsed.PrefabSpecJson);
        return hasFile == hasInline;
    }

    private static bool HasInvalidSceneSpecSource(ParsedCommand parsed)
    {
        bool hasFile = !string.IsNullOrWhiteSpace(parsed.SceneSpecFile);
        bool hasInline = !string.IsNullOrWhiteSpace(parsed.SceneSpecJson);
        return hasFile == hasInline;
    }

    private static bool ScenePatchContainsDestructiveOperation(ParsedCommand parsed)
    {
        string specJson;
        try
        {
            specJson = parsed.ResolveSceneSpecJson();
        }
        catch (CliUsageException)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(specJson);
            // TryGetProperty throws on a non-object root, and that escapes the JsonException
            // catch below. A spec we cannot read holds no destructive op; the editor rejects it.
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("operations", out JsonElement operations)
                || operations.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement operation in operations.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object
                    || !operation.TryGetProperty("op", out JsonElement opElement)
                    || opElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? op = opElement.GetString();
                if (string.Equals(op, "delete-gameobject", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(op, "remove-component", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool PrefabPatchContainsDestructiveOperation(ParsedCommand parsed)
    {
        string specJson;
        try
        {
            specJson = parsed.ResolvePrefabSpecJson();
        }
        catch (CliUsageException)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(specJson);
            // TryGetProperty throws on a non-object root, and that escapes the JsonException
            // catch below. A spec we cannot read holds no destructive op; the editor rejects it.
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("operations", out JsonElement operations)
                || operations.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement operation in operations.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object
                    || !operation.TryGetProperty("op", out JsonElement opElement)
                    || opElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? op = opElement.GetString();
                if (string.Equals(op, "remove-node", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(op, "remove-component", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
