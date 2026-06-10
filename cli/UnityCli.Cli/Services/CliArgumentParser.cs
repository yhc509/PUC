#nullable enable
using System.Linq;
using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

public static partial class CliArgumentParser
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedCommand(CommandKind.Help);
        }

        var tokens = new Queue<string>(args);
        var parsed = new ParsedCommand(CommandKind.Help);
        var outputMode = OutputMode.Default;
        string? projectOverride = null;

        while (tokens.Count > 0)
        {
            if (tokens.Peek() == "--json")
            {
                outputMode = OutputMode.Json;
                tokens.Dequeue();
                continue;
            }

            if (tokens.Peek() == "--output")
            {
                tokens.Dequeue();
                outputMode = RequireOutputMode(RequireValue(tokens, "--output"));
                continue;
            }

            if (tokens.Peek() == "--project")
            {
                tokens.Dequeue();
                projectOverride = RequireValue(tokens, "--project");
                continue;
            }

            break;
        }

        if (tokens.Count == 0)
        {
            parsed.OutputMode = outputMode;
            return parsed;
        }

        var command = tokens.Dequeue().ToLowerInvariant();
        parsed = command switch
        {
            "status" => new ParsedCommand(CommandKind.Status),
            "compile" => new ParsedCommand(CommandKind.Compile),
            "refresh" => new ParsedCommand(CommandKind.Refresh),
            "read-console" => new ParsedCommand(CommandKind.ReadConsole),
            "play" => new ParsedCommand(CommandKind.Play),
            "pause" => new ParsedCommand(CommandKind.Pause),
            "stop" => new ParsedCommand(CommandKind.Stop),
            "execute-menu" => new ParsedCommand(CommandKind.ExecuteMenu),
            "screenshot" => new ParsedCommand(CommandKind.Screenshot),
            "execute" => new ParsedCommand(CommandKind.ExecuteCode),
            "custom" => ParseCustom(tokens),
            "asset" => ParseAsset(tokens),
            "scene" => ParseScene(tokens),
            "prefab" => ParsePrefab(tokens),
            "test" => ParseTestCommand(tokens),
            "package" => ParsePackage(tokens),
            "material" => ParseMaterial(tokens),
            "qa" => ParseQa(tokens),
            "instances" => ParseInstances(tokens),
            "doctor" => new ParsedCommand(CommandKind.Doctor),
            "raw" => new ParsedCommand(CommandKind.Raw),
            "help" or "--help" or "-h" => new ParsedCommand(CommandKind.Help),
            _ => throw new CliUsageException($"알 수 없는 명령입니다: {command}"),
        };

        parsed.OutputMode = outputMode;
        parsed.ProjectOverride = projectOverride;
        ApplyCatalogDefaults(parsed);

        ParseCommandOptions(parsed, tokens);
        return parsed;
    }

    private static void ApplyCatalogDefaults(ParsedCommand parsed)
    {
        if (parsed.Kind == CommandKind.Help)
        {
            return;
        }

        CliCommandDescriptor descriptor = GetCatalogDescriptor(parsed.Kind);
        parsed.TimeoutMs = descriptor.DefaultLiveTimeoutMs ?? ProtocolConstants.DefaultLiveTimeoutMs;
    }

    public static string BuildHelpText()
    {
        string[] notes =
        [
            "qa tap and coordinate-based qa swipe treat coordinates as screenshot pixels with a top-left origin; pass screenshot image coordinates directly because the bridge handles Y-flip and scaling, auto-uses the last captured screenshot size when available, and still accepts --screenshot-width/--screenshot-height as explicit overrides.",
            "qa swipe --target still treats --from/--to as pixel offsets from the target RectTransform center.",
            "screenshot --view game in Play Mode captures at the native Game View size first; --width/--height only downscale it, and larger requests save at native size with a warning."
        ];

        return string.Join(
            Environment.NewLine,
            [
                CliCommandMetadata.BuildHelpText(),
                "notes:",
                ..notes,
                string.Empty,
            ]);
    }

    // Best-effort pre-parse for early error formatting before full command parsing.
    // Structural limitation: positional values can be mistaken for `--output` values,
    // so `execute --code --output compact` is detected as compact even though `--output`
    // should be consumed as the `--code` value. The old `DetectJsonOutput` pre-parse had
    // the same limitation, so this does not introduce a new regression.
    public static OutputMode DetectOutputMode(string[] args)
    {
        var tokens = new Queue<string>(args);
        var outputMode = OutputMode.Default;

        while (tokens.Count > 0)
        {
            if (tokens.Peek() == "--json")
            {
                outputMode = OutputMode.Json;
                tokens.Dequeue();
                continue;
            }

            if (tokens.Peek() == "--output")
            {
                tokens.Dequeue();
                if (tokens.Count == 0)
                {
                    break;
                }

                outputMode = TryGetOutputMode(tokens.Dequeue()) ?? outputMode;
                continue;
            }

            if (tokens.Peek() == "--project")
            {
                tokens.Dequeue();
                if (tokens.Count > 0)
                {
                    tokens.Dequeue();
                }

                continue;
            }

            break;
        }

        if (tokens.Count == 0)
        {
            return outputMode;
        }

        var command = tokens.Dequeue().ToLowerInvariant();
        while (tokens.Count > 0)
        {
            var token = tokens.Dequeue();
            if (token == "--output")
            {
                if (tokens.Count == 0)
                {
                    break;
                }

                outputMode = TryGetOutputMode(tokens.Dequeue()) ?? outputMode;
                continue;
            }

            if (token == "--json" && command is not ("raw" or "custom"))
            {
                outputMode = OutputMode.Json;
            }
        }

        return outputMode;
    }

    private static ParsedCommand ParseInstances(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`instances` 다음에는 `list` 또는 `use`가 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "list" => new ParsedCommand(CommandKind.InstancesList),
            "use" => new ParsedCommand(CommandKind.InstancesUse)
            {
                InstanceTarget = tokens.Count > 0 ? tokens.Dequeue() : null,
            },
            _ => throw new CliUsageException($"알 수 없는 instances 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParseAsset(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`asset` 다음에는 하위 명령이 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "find" => new ParsedCommand(CommandKind.AssetFind),
            "types" => new ParsedCommand(CommandKind.AssetTypes),
            "info" => new ParsedCommand(CommandKind.AssetInfo),
            "reimport" => new ParsedCommand(CommandKind.AssetReimport),
            "mkdir" => new ParsedCommand(CommandKind.AssetMkdir),
            "move" => new ParsedCommand(CommandKind.AssetMove),
            "rename" => new ParsedCommand(CommandKind.AssetRename),
            "delete" => new ParsedCommand(CommandKind.AssetDelete),
            "create" => new ParsedCommand(CommandKind.AssetCreate),
            _ => throw new CliUsageException($"알 수 없는 asset 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParseScene(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`scene` 다음에는 하위 명령이 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "open" => new ParsedCommand(CommandKind.SceneOpen),
            "inspect" => new ParsedCommand(CommandKind.SceneInspect),
            "patch" => new ParsedCommand(CommandKind.ScenePatch),
            "add-object" => new ParsedCommand(CommandKind.SceneAddObject),
            "set-transform" => new ParsedCommand(CommandKind.SceneSetTransform),
            "add-component" => new ParsedCommand(CommandKind.SceneAddComponent),
            "remove-component" => new ParsedCommand(CommandKind.SceneRemoveComponent),
            "list-components" => new ParsedCommand(CommandKind.SceneListComponents),
            "assign-material" => new ParsedCommand(CommandKind.SceneAssignMaterial),
            _ => throw new CliUsageException($"알 수 없는 scene 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParsePrefab(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`prefab` 다음에는 하위 명령이 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "inspect" => new ParsedCommand(CommandKind.PrefabInspect),
            "create" => new ParsedCommand(CommandKind.PrefabCreate),
            "patch" => new ParsedCommand(CommandKind.PrefabPatch),
            "add-component" => new ParsedCommand(CommandKind.PrefabAddComponent),
            "remove-component" => new ParsedCommand(CommandKind.PrefabRemoveComponent),
            "list-components" => new ParsedCommand(CommandKind.PrefabListComponents),
            _ => throw new CliUsageException($"알 수 없는 prefab 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParsePackage(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`package` 다음에는 하위 명령이 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "list" => new ParsedCommand(CommandKind.PackageList),
            "add" => new ParsedCommand(CommandKind.PackageAdd),
            "remove" => new ParsedCommand(CommandKind.PackageRemove),
            "search" => new ParsedCommand(CommandKind.PackageSearch),
            _ => throw new CliUsageException($"알 수 없는 package 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParseTestCommand(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`test` 다음에는 `list`, `run`, `results` 중 하나가 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "list" => ParseTestList(),
            "run" => ParseTestRun(),
            "results" => ParseTestResults(),
            _ => throw new CliUsageException($"알 수 없는 test 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParseTestList()
    {
        return new ParsedCommand(CommandKind.TestList)
        {
            TestMode = "all",
        };
    }

    private static ParsedCommand ParseTestRun()
    {
        return new ParsedCommand(CommandKind.TestRun);
    }

    private static ParsedCommand ParseTestResults()
    {
        return new ParsedCommand(CommandKind.TestResults);
    }

    private static ParsedCommand ParseMaterial(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`material` 다음에는 하위 명령이 필요합니다.");
        }

        var subCommand = tokens.Dequeue().ToLowerInvariant();
        return subCommand switch
        {
            "info" => new ParsedCommand(CommandKind.MaterialInfo),
            "set" => new ParsedCommand(CommandKind.MaterialSet),
            _ => throw new CliUsageException($"알 수 없는 material 하위 명령입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParseQa(Queue<string> tokens)
    {
        string subCommand = RequireSubcommand(tokens, "qa");
        return subCommand switch
        {
            "click" => new ParsedCommand(CommandKind.QaClick),
            "tap" => new ParsedCommand(CommandKind.QaTap),
            "swipe" => new ParsedCommand(CommandKind.QaSwipe),
            "key" => new ParsedCommand(CommandKind.QaKey),
            "ui-dump" => new ParsedCommand(CommandKind.QaUiDump),
            "wait" => new ParsedCommand(CommandKind.QaWait),
            "wait-until" => new ParsedCommand(CommandKind.QaWaitUntil),
            _ => throw new CliUsageException($"알 수 없는 qa 서브커맨드입니다: {subCommand}"),
        };
    }

    private static ParsedCommand ParseCustom(Queue<string> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new CliUsageException("`custom` 다음에는 명령 이름이 필요합니다.");
        }

        var commandName = tokens.Dequeue();
        return new ParsedCommand(CommandKind.Custom)
        {
            CustomCommandName = commandName,
        };
    }

    private static void ParseCommandOptions(ParsedCommand parsed, Queue<string> tokens)
    {
        while (tokens.Count > 0)
        {
            var token = tokens.Dequeue();
            if (token == "--project")
            {
                parsed.ProjectOverride = RequireValue(tokens, "--project");
                continue;
            }

            if (token == "--timeout-ms")
            {
                parsed.TimeoutMs = RequireInt(RequireValue(tokens, "--timeout-ms"), "--timeout-ms");
                parsed.TimeoutMsSpecified = true;
                continue;
            }

            if (token == "--output")
            {
                parsed.OutputMode = RequireOutputMode(RequireValue(tokens, "--output"));
                continue;
            }

            if (token == "--json" && parsed.Kind != CommandKind.Raw && parsed.Kind != CommandKind.Custom)
            {
                parsed.OutputMode = OutputMode.Json;
                continue;
            }

            switch (parsed.Kind)
            {
                case CommandKind.Raw when token == "--json":
                    parsed.RawJson = RequireValue(tokens, "--json");
                    break;
                case CommandKind.Raw when token == "--force":
                    parsed.Force = true;
                    break;
                case CommandKind.Compile when token == "--wait":
                case CommandKind.Refresh when token == "--wait":
                    parsed.Wait = true;
                    break;
                case CommandKind.ReadConsole when token == "--limit":
                    parsed.ConsoleLimit = RequireInt(RequireValue(tokens, "--limit"), "--limit");
                    break;
                case CommandKind.ReadConsole when token == "--type":
                    parsed.ConsoleType = RequireValue(tokens, "--type");
                    break;
                case CommandKind.ExecuteMenu when token == "--path":
                    parsed.MenuPath = RequireValue(tokens, "--path");
                    break;
                case CommandKind.ExecuteMenu when token == "--list":
                    parsed.MenuList = true;
                    parsed.MenuListPrefix = RequireValue(tokens, "--list");
                    break;
                case CommandKind.Screenshot when token == "--view":
                    parsed.ScreenshotView = RequireScreenshotView(RequireValue(tokens, "--view"));
                    break;
                case CommandKind.Screenshot when token == "--camera":
                    parsed.ScreenshotCamera = RequireValue(tokens, "--camera");
                    break;
                case CommandKind.Screenshot when token == "--path":
                    parsed.ScreenshotPath = RequireValue(tokens, "--path");
                    break;
                case CommandKind.Screenshot when token == "--width":
                    parsed.ScreenshotWidth = RequireInt(RequireValue(tokens, "--width"), "--width");
                    break;
                case CommandKind.Screenshot when token == "--height":
                    parsed.ScreenshotHeight = RequireInt(RequireValue(tokens, "--height"), "--height");
                    break;
                case CommandKind.ExecuteCode when token == "--code":
                    parsed.ExecuteCodeSnippet = RequireValue(tokens, "--code");
                    break;
                case CommandKind.ExecuteCode when token == "--file":
                    parsed.ExecuteCodeFile = RequireValue(tokens, "--file");
                    break;
                case CommandKind.ExecuteCode when token == "--args":
                    parsed.ExecuteCodeArgsJson = RequireJsonValue(tokens, "--args");
                    break;
                case CommandKind.ExecuteCode when token == "--timeout":
                    parsed.ExecuteCodeTimeoutSeconds = RequireInt(RequireValue(tokens, "--timeout"), "--timeout");
                    break;
                case CommandKind.ExecuteCode when token == "--force":
                    parsed.Force = true;
                    break;
                case CommandKind.Custom when token == "--json":
                    parsed.CustomArgsJson = RequireValue(tokens, "--json");
                    break;
                case CommandKind.PackageAdd when token == "--name":
                    parsed.PackageName = RequireValue(tokens, "--name");
                    break;
                case CommandKind.PackageAdd when token == "--version":
                    parsed.PackageVersion = RequireValue(tokens, "--version");
                    break;
                case CommandKind.PackageRemove when token == "--name":
                    parsed.PackageName = RequireValue(tokens, "--name");
                    break;
                case CommandKind.PackageRemove when token == "--force":
                    parsed.Force = true;
                    break;
                case CommandKind.PackageSearch when token == "--query":
                    parsed.PackageQuery = RequireValue(tokens, "--query");
                    break;
                case CommandKind.TestList when token == "--mode":
                    parsed.TestMode = RequireTestMode(RequireValue(tokens, "--mode"), allowAll: true);
                    break;
                case CommandKind.TestRun when token == "--mode":
                    parsed.TestMode = RequireTestMode(RequireValue(tokens, "--mode"), allowAll: false);
                    break;
                case CommandKind.TestRun when token == "--filter":
                    parsed.TestFilter = RequireValue(tokens, "--filter");
                    break;
                case CommandKind.TestRun when token == "--category":
                    parsed.TestCategory = RequireValue(tokens, "--category");
                    break;
                case CommandKind.TestRun when token == "--assembly":
                    parsed.TestAssembly = RequireValue(tokens, "--assembly");
                    break;
                case CommandKind.TestRun when token == "--no-domain-reload":
                    parsed.TestNoDomainReload = true;
                    break;
                case CommandKind.TestRun when token == "--timeout":
                    parsed.TestTimeoutSeconds = RequireInt(RequireValue(tokens, "--timeout"), "--timeout");
                    break;
                case CommandKind.TestRun when token == "--wait":
                    parsed.TestWait = true;
                    break;
                case CommandKind.TestResults when token == "--run-id":
                    parsed.TestRunId = RequireValue(tokens, "--run-id");
                    break;
                case CommandKind.MaterialInfo when token == "--path":
                case CommandKind.MaterialSet when token == "--path":
                    parsed.MaterialPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.MaterialInfo when token == "--omit-defaults":
                    parsed.OmitDefaults = true;
                    break;
                case CommandKind.MaterialSet when token == "--property":
                    parsed.MaterialProperty = RequireValue(tokens, "--property");
                    break;
                case CommandKind.MaterialSet when token == "--value":
                    parsed.MaterialValue = RequireValue(tokens, "--value");
                    break;
                case CommandKind.MaterialSet when token == "--texture":
                    parsed.MaterialTexture = RequireValue(tokens, "--texture");
                    break;
                case CommandKind.MaterialSet when token == "--asset":
                    parsed.MaterialTextureAsset = RequireAssetPath(RequireValue(tokens, "--asset"), "--asset");
                    break;
                case CommandKind.QaClick when token == "--qa-id":
                case CommandKind.QaWaitUntil when token == "--qa-id":
                    parsed.QaId = RequireValue(tokens, "--qa-id");
                    break;
                case CommandKind.QaClick when token == "--target":
                case CommandKind.QaSwipe when token == "--target":
                    parsed.QaTarget = RequireValue(tokens, "--target");
                    break;
                case CommandKind.QaTap when token == "--x":
                    parsed.QaTapX = RequireInt(RequireValue(tokens, "--x"), "--x", minimumValue: null);
                    break;
                case CommandKind.QaTap when token == "--y":
                    parsed.QaTapY = RequireInt(RequireValue(tokens, "--y"), "--y", minimumValue: null);
                    break;
                case CommandKind.QaTap when token == "--screenshot-width":
                case CommandKind.QaUiDump when token == "--screenshot-width":
                case CommandKind.QaSwipe when token == "--screenshot-width":
                    parsed.QaScreenshotWidth = RequireInt(RequireValue(tokens, "--screenshot-width"), "--screenshot-width");
                    break;
                case CommandKind.QaTap when token == "--screenshot-height":
                case CommandKind.QaUiDump when token == "--screenshot-height":
                case CommandKind.QaSwipe when token == "--screenshot-height":
                    parsed.QaScreenshotHeight = RequireInt(RequireValue(tokens, "--screenshot-height"), "--screenshot-height");
                    break;
                case CommandKind.QaSwipe when token == "--from":
                    parsed.QaSwipeFrom = RequireValue(tokens, "--from");
                    break;
                case CommandKind.QaSwipe when token == "--to":
                    parsed.QaSwipeTo = RequireValue(tokens, "--to");
                    break;
                case CommandKind.QaSwipe when token == "--duration":
                    parsed.QaSwipeDuration = RequireInt(RequireValue(tokens, "--duration"), "--duration");
                    break;
                case CommandKind.QaKey when token == "--key":
                    parsed.QaKeyName = RequireValue(tokens, "--key");
                    break;
                case CommandKind.QaWait when token == "--ms":
                    parsed.QaWaitMs = RequireInt(RequireValue(tokens, "--ms"), "--ms");
                    break;
                case CommandKind.QaWaitUntil when token == "--scene":
                    parsed.QaWaitScene = RequireValue(tokens, "--scene");
                    break;
                case CommandKind.QaWaitUntil when token == "--log-contains":
                    parsed.QaWaitLogContains = RequireValue(tokens, "--log-contains");
                    break;
                case CommandKind.QaWaitUntil when token == "--object-exists":
                    parsed.QaWaitObjectExists = RequireValue(tokens, "--object-exists");
                    break;
                case CommandKind.QaWaitUntil when token == "--timeout":
                    parsed.QaWaitTimeout = RequireInt(RequireValue(tokens, "--timeout"), "--timeout");
                    break;
                case CommandKind.AssetFind when token == "--name":
                    parsed.AssetName = RequireValue(tokens, "--name");
                    break;
                case CommandKind.AssetFind when token == "--type":
                    parsed.AssetType = RequireValue(tokens, "--type");
                    break;
                case CommandKind.AssetFind when token == "--folder":
                    parsed.AssetFolder = RequireAssetPath(RequireValue(tokens, "--folder"), "--folder");
                    break;
                case CommandKind.AssetFind when token == "--limit":
                    parsed.AssetLimit = RequireInt(RequireValue(tokens, "--limit"), "--limit");
                    break;
                case CommandKind.AssetInfo when token == "--path":
                    parsed.AssetPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path", allowPackages: true);
                    break;
                case CommandKind.AssetInfo when token == "--guid":
                    parsed.AssetGuid = RequireValue(tokens, "--guid");
                    break;
                case CommandKind.AssetReimport when token == "--path":
                case CommandKind.AssetMkdir when token == "--path":
                case CommandKind.AssetDelete when token == "--path":
                    parsed.AssetPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.AssetMove when token == "--from":
                    parsed.AssetFrom = RequireAssetPath(RequireValue(tokens, "--from"), "--from");
                    break;
                case CommandKind.AssetMove when token == "--to":
                    parsed.AssetTo = RequireAssetPath(RequireValue(tokens, "--to"), "--to");
                    break;
                case CommandKind.AssetRename when token == "--path":
                    parsed.AssetPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.AssetRename when token == "--name":
                    parsed.AssetNewName = RequireValue(tokens, "--name");
                    break;
                case CommandKind.AssetMove when token == "--force":
                case CommandKind.AssetRename when token == "--force":
                case CommandKind.AssetDelete when token == "--force":
                case CommandKind.AssetCreate when token == "--force":
                    parsed.Force = true;
                    break;
                case CommandKind.AssetCreate when token == "--type":
                    parsed.AssetCreateType = RequireAssetCreateType(RequireValue(tokens, "--type"));
                    break;
                case CommandKind.AssetCreate when token == "--path":
                    parsed.AssetPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.AssetCreate when token == "--data-json":
                    parsed.AssetDataJson = RequireValue(tokens, "--data-json");
                    break;
                case CommandKind.AssetCreate when token == "--shader":
                    parsed.AssetShader = RequireValue(tokens, "--shader");
                    break;
                case CommandKind.AssetCreate when token == "--script":
                    parsed.AssetScript = RequireAssetPath(RequireValue(tokens, "--script"), "--script");
                    break;
                case CommandKind.AssetCreate when token == "--type-name":
                    parsed.AssetTypeName = RequireValue(tokens, "--type-name");
                    break;
                case CommandKind.AssetCreate when token == "--legacy":
                    parsed.AssetLegacy = true;
                    break;
                case CommandKind.AssetCreate when token == "--initial-map":
                    parsed.AssetInitialMap = RequireValue(tokens, "--initial-map");
                    break;
                case CommandKind.AssetCreate when token == "--root-name":
                    parsed.AssetRootName = RequireValue(tokens, "--root-name");
                    break;
                case CommandKind.AssetCreate when token == "--base-controller":
                    parsed.AssetBaseController = RequireAssetPath(RequireValue(tokens, "--base-controller"), "--base-controller");
                    break;
                case CommandKind.AssetCreate when token == "--width":
                    parsed.AssetWidth = RequireInt(RequireValue(tokens, "--width"), "--width");
                    break;
                case CommandKind.AssetCreate when token == "--height":
                    parsed.AssetHeight = RequireInt(RequireValue(tokens, "--height"), "--height");
                    break;
                case CommandKind.AssetCreate when token == "--depth":
                    parsed.AssetDepth = RequireInt(RequireValue(tokens, "--depth"), "--depth", minimumValue: 0);
                    break;
                case CommandKind.SceneOpen when token == "--path":
                case CommandKind.SceneInspect when token == "--path":
                case CommandKind.ScenePatch when token == "--path":
                case CommandKind.SceneAddObject when token == "--path":
                case CommandKind.SceneAddComponent when token == "--path":
                case CommandKind.SceneRemoveComponent when token == "--path":
                    parsed.ScenePath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.SceneOpen when token == "--force":
                case CommandKind.ScenePatch when token == "--force":
                case CommandKind.SceneRemoveComponent when token == "--force":
                    parsed.Force = true;
                    break;
                case CommandKind.SceneInspect when token == "--with-values":
                    parsed.SceneWithValues = true;
                    break;
                case CommandKind.SceneInspect when token == "--max-depth":
                    parsed.MaxDepth = RequireInt(RequireValue(tokens, "--max-depth"), "--max-depth");
                    break;
                case CommandKind.SceneInspect when token == "--omit-defaults":
                    parsed.OmitDefaults = true;
                    break;
                case CommandKind.ScenePatch when token == "--spec-file":
                    parsed.SceneSpecFile = RequireValue(tokens, "--spec-file");
                    break;
                case CommandKind.ScenePatch when token == "--spec-json":
                    parsed.SceneSpecJson = RequireValue(tokens, "--spec-json");
                    break;
                case CommandKind.SceneAddObject when token == "--parent":
                    parsed.SceneParent = RequireValue(tokens, "--parent");
                    break;
                case CommandKind.SceneAddObject when token == "--name":
                    parsed.SceneObjectName = RequireValue(tokens, "--name");
                    break;
                case CommandKind.SceneAddObject when token == "--primitive":
                    parsed.ScenePrimitive = RequireScenePrimitive(RequireValue(tokens, "--primitive"));
                    break;
                case CommandKind.SceneAddObject when token == "--components":
                    parsed.SceneComponents = RequireValue(tokens, "--components");
                    break;
                case CommandKind.SceneAddComponent when token == "--node":
                case CommandKind.SceneRemoveComponent when token == "--node":
                case CommandKind.SceneListComponents when token == "--node":
                case CommandKind.SceneSetTransform when token == "--node":
                case CommandKind.SceneAssignMaterial when token == "--node":
                    parsed.SceneTarget = RequireValue(tokens, "--node");
                    break;
                case CommandKind.SceneAddObject when token == "--position":
                    parsed.ScenePosition = RequireValue(tokens, "--position");
                    break;
                case CommandKind.SceneSetTransform when token == "--position":
                    parsed.ScenePosition = RequireValue(tokens, "--position");
                    break;
                case CommandKind.SceneSetTransform when token == "--rotation":
                    parsed.SceneRotation = RequireValue(tokens, "--rotation");
                    break;
                case CommandKind.SceneSetTransform when token == "--scale":
                    parsed.SceneScale = RequireValue(tokens, "--scale");
                    break;
                case CommandKind.SceneAddComponent when token == "--type":
                case CommandKind.SceneRemoveComponent when token == "--type":
                    parsed.SceneComponentType = RequireValue(tokens, "--type");
                    break;
                case CommandKind.SceneRemoveComponent when token == "--index":
                case CommandKind.PrefabRemoveComponent when token == "--index":
                    parsed.SceneComponentIndex = RequireInt(RequireValue(tokens, "--index"), "--index", minimumValue: 0);
                    break;
                case CommandKind.SceneAddComponent when token == "--values":
                    parsed.SceneComponentValues = RequireValue(tokens, "--values");
                    break;
                case CommandKind.SceneAssignMaterial when token == "--material":
                    parsed.MaterialPath = RequireAssetPath(RequireValue(tokens, "--material"), "--material");
                    break;
                case CommandKind.PrefabInspect when token == "--path":
                    parsed.PrefabPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.PrefabInspect when token == "--with-values":
                    parsed.PrefabWithValues = true;
                    break;
                case CommandKind.PrefabInspect when token == "--max-depth":
                    parsed.MaxDepth = RequireInt(RequireValue(tokens, "--max-depth"), "--max-depth");
                    break;
                case CommandKind.PrefabInspect when token == "--omit-defaults":
                    parsed.OmitDefaults = true;
                    break;
                case CommandKind.PrefabCreate when token == "--path":
                case CommandKind.PrefabPatch when token == "--path":
                case CommandKind.PrefabAddComponent when token == "--path":
                case CommandKind.PrefabRemoveComponent when token == "--path":
                case CommandKind.PrefabListComponents when token == "--path":
                    parsed.PrefabPath = RequireAssetPath(RequireValue(tokens, "--path"), "--path");
                    break;
                case CommandKind.PrefabCreate when token == "--force":
                case CommandKind.PrefabPatch when token == "--force":
                case CommandKind.PrefabRemoveComponent when token == "--force":
                    parsed.Force = true;
                    break;
                case CommandKind.PrefabCreate when token == "--spec-file":
                case CommandKind.PrefabPatch when token == "--spec-file":
                    parsed.PrefabSpecFile = RequireValue(tokens, "--spec-file");
                    break;
                case CommandKind.PrefabCreate when token == "--spec-json":
                case CommandKind.PrefabPatch when token == "--spec-json":
                    parsed.PrefabSpecJson = RequireValue(tokens, "--spec-json");
                    break;
                case CommandKind.PrefabAddComponent when token == "--node":
                case CommandKind.PrefabRemoveComponent when token == "--node":
                case CommandKind.PrefabListComponents when token == "--node":
                    parsed.SceneTarget = RequireValue(tokens, "--node");
                    break;
                case CommandKind.PrefabAddComponent when token == "--type":
                case CommandKind.PrefabRemoveComponent when token == "--type":
                    parsed.SceneComponentType = RequireValue(tokens, "--type");
                    break;
                case CommandKind.PrefabAddComponent when token == "--values":
                    parsed.SceneComponentValues = RequireValue(tokens, "--values");
                    break;
                default:
                    if (parsed.Kind == CommandKind.AssetCreate && token.StartsWith("--", StringComparison.Ordinal))
                    {
                        AddAssetCreateCustomOption(parsed, token, tokens);
                        break;
                    }

                    throw new CliUsageException($"지원하지 않는 옵션입니다: {token}");
            }
        }

        ValidateExecuteMenuOptions(parsed);
        ValidateAssetOptions(parsed);
        ValidateSceneOptions(parsed);
        ValidateScreenshotOptions(parsed);
        ValidatePackageOptions(parsed);
        ValidateTestOptions(parsed);
        ValidateMaterialOptions(parsed);
        ValidateQaOptions(parsed);
        ValidateExecuteOptions(parsed);

        if (parsed.Kind == CommandKind.Raw && string.IsNullOrWhiteSpace(parsed.RawJson))
        {
            throw new CliUsageException("`raw`에는 `--json` payload가 필요합니다.");
        }
    }

    private static void AddAssetCreateCustomOption(ParsedCommand parsed, string token, Queue<string> tokens)
    {
        string optionName = NormalizeCustomOptionName(token);
        object value = true;
        if (tokens.Count > 0 && !tokens.Peek().StartsWith("--", StringComparison.Ordinal))
        {
            value = ParseCustomOptionValue(tokens.Dequeue());
        }

        parsed.AssetCustomOptions[optionName] = value;
    }

    private static object ParseCustomOptionValue(string rawValue)
    {
        if (bool.TryParse(rawValue, out bool boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(rawValue, out int intValue))
        {
            return intValue;
        }

        if (double.TryParse(rawValue, out double doubleValue))
        {
            return doubleValue;
        }

        return rawValue;
    }

    private static string NormalizeCustomOptionName(string token)
    {
        string trimmed = token.Trim();
        if (!trimmed.StartsWith("--", StringComparison.Ordinal) || trimmed.Length <= 2)
        {
            throw new CliUsageException($"지원하지 않는 옵션입니다: {token}");
        }

        string body = trimmed[2..];
        string[] parts = body.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new CliUsageException($"지원하지 않는 옵션입니다: {token}");
        }

        return string.Concat(parts.Select((part, index) =>
            index == 0
                ? char.ToLowerInvariant(part[0]) + part[1..]
                : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool IsBuiltInAssetCreateType(string? type)
    {
        return BuiltInAssetCreateCatalog.IsBuiltInType(type);
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static OutputMode RequireOutputMode(string value)
    {
        if (value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException("`--output`에 값이 필요합니다. `default`, `json`, `compact` 중 하나를 지정하세요.");
        }

        return TryGetOutputMode(value)
            ?? throw new CliUsageException("`--output`은 `default`, `json`, `compact` 중 하나여야 합니다.");
    }

    private static OutputMode? TryGetOutputMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "default" => OutputMode.Default,
            "json" => OutputMode.Json,
            "compact" => OutputMode.Compact,
            _ => null,
        };
    }

}
