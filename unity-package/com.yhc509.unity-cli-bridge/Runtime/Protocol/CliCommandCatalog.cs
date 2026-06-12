#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace UnityCli.Protocol
{
    public enum CliCommandGroup
    {
        EditorControl,
        AssetWorkflows,
        SceneWorkflows,
        PrefabWorkflows,
        InstanceManagement,
        Diagnostics,
        PackageManagement,
        MaterialWorkflows,
        QaWorkflows,
    }

    public enum ForceRule
    {
        None,
        OnOverwrite,
        OnDestructiveOp,
        Always,
    }

    public sealed class CliCommandDescriptor
    {
        public CliCommandDescriptor(
            string command,
            string synopsis,
            string summary,
            CliCommandGroup group,
            string? protocolCommand,
            bool canUseLocal,
            bool canUseLive,
            bool isAllowedWhileBusy,
            string[]? notes = null,
            ForceRule forceRule = ForceRule.None,
            int? defaultLiveTimeoutMs = null)
        {
            Command = command;
            Synopsis = synopsis;
            Summary = summary;
            Group = group;
            ProtocolCommand = protocolCommand;
            CanUseLocal = canUseLocal;
            CanUseLive = canUseLive;
            IsAllowedWhileBusy = isAllowedWhileBusy;
            Notes = notes ?? Array.Empty<string>();
            ForceRule = forceRule;
            DefaultLiveTimeoutMs = defaultLiveTimeoutMs;
        }

        public string Command { get; }

        public string Synopsis { get; }

        public string Summary { get; }

        public CliCommandGroup Group { get; }

        public string? ProtocolCommand { get; }

        public bool CanUseLocal { get; }

        public bool CanUseLive { get; }
        public bool IsAllowedWhileBusy { get; }
        public ForceRule ForceRule { get; }
        public int? DefaultLiveTimeoutMs { get; }

        [Obsolete("Use ForceRule instead.")]
        public bool RequiresForce => ForceRule != ForceRule.None;

        public string[] Notes { get; }
    }

    public static class CliCommandCatalog
    {
        private static readonly CliCommandDescriptor[] _commands =
        {
            new CliCommandDescriptor(
                "status",
                "status",
                "Reports the selected project and live editor state when a running bridge is reachable, with a local fallback when it is not.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandStatus,
                canUseLocal: true,
                canUseLive: true,
                isAllowedWhileBusy: true,
                notes: new[] { "Falls back to local registry and Unity-path inspection when no live editor is reachable." }),
            new CliCommandDescriptor(
                "compile",
                "compile [--wait]",
                "Triggers a script compile in the running editor. Add --wait to block until compiling/importing finishes and the bridge is reachable again.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandCompile,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "--wait polls status every 2s for up to 120s after the compile request succeeds." }),
            new CliCommandDescriptor(
                "refresh",
                "refresh [--wait]",
                "Refreshes the AssetDatabase in the running editor. Add --wait to block until compiling/importing finishes and the bridge is reachable again.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandRefresh,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "--wait polls status every 2s for up to 120s after the refresh request succeeds." }),
            new CliCommandDescriptor(
                "read-console",
                "read-console [--limit N] [--type log|warning|error]",
                "Reads recent editor console entries from a running editor.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandReadConsole,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true),
            new CliCommandDescriptor(
                "play",
                "play",
                "Starts Play Mode in a running editor.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandPlay,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "pause",
                "pause",
                "Pauses Play Mode in a running editor.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandPause,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "stop",
                "stop",
                "Stops Play Mode in a running editor.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandStop,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "execute-menu",
                "execute-menu (--path \"Menu/Item\" | --list \"Prefix\")",
                "Executes a Unity menu item or lists registered menu items matching a prefix in a running editor.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandExecuteMenu,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Use --list to inspect registered menu item paths before executing one." }),
            new CliCommandDescriptor(
                "screenshot",
                "screenshot [--view game|scene (default: game) | --camera <name>] [--path <output.png|output.jpg>] [--width N] [--height N] [--format png|jpg|jpeg] [--quality 1-100] [--max-width N]",
                "Captures a screenshot from the Game View, Scene View, or a named camera. Defaults to Game View; encoding defaults to PNG. Use --format jpg with --quality to reduce file size, and --max-width to downscale proportionally when --width/--height are not specified. The response includes image size, actual saved format, and screen-space metadata (`screenWidth`, `screenHeight`, `imageOrigin`, `coordinateOrigin`) for QA coordinate alignment. In Play Mode, --view game can downscale the native Game View capture but does not upscale it.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandScreenshot,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[]
                {
                    "Live-only.",
                    "--format controls encoding regardless of --path extension; temporary paths use .png or .jpg to match the selected format.",
                    "--max-width is ignored when --width or --height is specified.",
                    "Play Mode --view game captures at the native Game View size first; larger --width/--height requests warn and save at native resolution instead of upscaling.",
                }),
            new CliCommandDescriptor(
                "execute",
                "execute (--code <csharp> | --file <path>) [--args <json>] [--timeout <초>] --force",
                "Executes arbitrary C# code in the running editor context with optional JSON arguments, structured __pucResult return values, and an optional cooperative timeout; always requires --force.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandExecuteCode,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[]
                {
                    "Live-only.",
                    "Always requires --force as a safety gate.",
                    "--args JSON은 사용자 코드에서 __pucArgsJson 문자열 변수로 읽을 수 있습니다.",
                    "__pucResult에 값을 담으면 응답 result에 타입 보존 JSON이 반환됩니다. float는 G9, double은 G17 round-trip 포맷을 사용합니다.",
                    "--timeout (default 30초, 상한 600초)은 협력적 cancel입니다. 사용자 코드가 __pucToken (System.Threading.CancellationToken)을 직접 체크해야 강제 종료됩니다 — 체크하지 않으면 main thread를 계속 점유하므로 force 사용자 책임입니다.",
                    "wrapper 예약 prefix `__puc_internal_*` 변수와 `__pucToken`, `__pucResult` 변수는 사용자 코드에서 선언하지 마세요.",
                    "--args 값에는 secret/credential을 넣지 마세요. CodeDOM 컴파일 중 OS temp에 .cs 파일이 잠시 생성될 수 있습니다.",
                    "C# 5.0 이하 문법만 지원합니다 (CodeDOM 제한).",
                },
                forceRule: ForceRule.Always),
            new CliCommandDescriptor(
                "custom",
                "custom <command-name> [--json <args>]",
                "Invokes a project-defined custom command registered via [PucCommand] attribute.",
                CliCommandGroup.EditorControl,
                ProtocolConstants.CommandCustom,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[]
                {
                    "Live-only.",
                    "Custom commands are registered via [PucCommand(\"name\")] attribute on static methods.",
                    "Editor-assembly custom commands can return ExecuteValueSerializer.Serialize(obj); runtime-assembly commands must serialize precise JSON themselves.",
                }),
            new CliCommandDescriptor(
                "asset find",
                "asset find [--name <term>] [--type <type>] [--folder <Assets/...>] [--limit N]",
                "Finds assets by name and/or type, with an optional folder filter. Requires at least one of --name or --type.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetFind,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true),
            new CliCommandDescriptor(
                "asset types",
                "asset types",
                "Lists built-in and project extension asset-create type descriptors available to the target project.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetTypes,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true),
            new CliCommandDescriptor(
                "asset info",
                "asset info (--path <Assets/...|Packages/...> | --guid <guid>)",
                "Reads asset metadata by path or GUID. Query paths may point to package assets.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetInfo,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true),
            new CliCommandDescriptor(
                "asset reimport",
                "asset reimport --path <Assets/...>",
                "Reimports an existing asset.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetReimport,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "asset mkdir",
                "asset mkdir --path <Assets/...>",
                "Creates missing folders under `Assets/...`.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetMkdir,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "asset move",
                "asset move --from <Assets/...> --to <Assets/...> [--force]",
                "Moves an asset to a new path; overwriting the destination requires --force.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetMove,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Overwriting an existing target requires --force." },
                forceRule: ForceRule.OnOverwrite),
            new CliCommandDescriptor(
                "asset rename",
                "asset rename --path <Assets/...> --name <newName> [--force]",
                "Renames an asset in place; overwriting the destination requires --force.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetRename,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Overwriting an existing target requires --force." },
                forceRule: ForceRule.OnOverwrite),
            new CliCommandDescriptor(
                "asset delete",
                "asset delete --path <Assets/...> --force",
                "Deletes an asset and always requires --force.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetDelete,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Deletion is always gated by --force." },
                forceRule: ForceRule.Always),
            new CliCommandDescriptor(
                "asset create",
                "asset create --type <kind> --path <Assets/...> [--data-json <json>] [options]",
                "Creates a built-in or extension asset type; overwriting an existing asset requires --force.",
                CliCommandGroup.AssetWorkflows,
                ProtocolConstants.CommandAssetCreate,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "This repo ships the built-in asset types documented below.", "Runtime extension providers can add more types." },
                forceRule: ForceRule.OnOverwrite),
            new CliCommandDescriptor(
                "scene open",
                "scene open --path <Assets/...> [--force]",
                "Opens a saved scene asset; use --force to discard dirty loaded scenes.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandSceneOpen,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "scene inspect",
                "scene inspect --path <Assets/...> [--with-values] [--max-depth <N>] [--omit-defaults]",
                "Inspects a saved scene hierarchy; use --with-values when authoring scene patch specs and the other options to reduce payload size.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandSceneInspect,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Use --with-values before authoring a patch spec.", "Use --max-depth and --omit-defaults to reduce inspect payload size.", "Output with --omit-defaults is read-only; omitted fields are not restored when used as patch input.", "Detailed scene patch rules live in docs/scene-spec.md." }),
            new CliCommandDescriptor(
                "scene patch",
                "scene patch --path <Assets/...> (--spec-file <file.json> | --spec-json <json>) [--force]",
                "Applies a deterministic scene patch spec; destructive operations require --force.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandScenePatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Detailed scene patch rules live in docs/scene-spec.md." },
                forceRule: ForceRule.OnDestructiveOp),
            new CliCommandDescriptor(
                "scene add-object",
                "scene add-object --path <Assets/...> [--parent <scenePath>] --name <name> [--primitive <Cube|Sphere|Capsule|Cylinder|Plane|Quad>] [--position x,y,z] [--components \"Type1,Type2\"]",
                "Adds a new GameObject or built-in primitive to a scene; shortcut for a single add-gameobject scene patch operation.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandScenePatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Internally delegates to scene patch." }),
            new CliCommandDescriptor(
                "scene set-transform",
                "scene set-transform --node <scenePath> [--position x,y,z] [--rotation x,y,z] [--scale x,y,z]",
                "Sets local transform values on a node in the active loaded scene and saves the scene immediately.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandSceneSetTransform,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Requires at least one of --position, --rotation, or --scale.", "Uses the active scene instead of --path.", "Saves the active scene immediately after mutation." }),
            new CliCommandDescriptor(
                "scene add-component",
                "scene add-component --path <Assets/...> --node <scenePath> --type <ComponentType> [--values <json>]",
                "Adds a component to a GameObject; shortcut for a single add-component scene patch operation.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandScenePatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Internally delegates to scene patch." }),
            new CliCommandDescriptor(
                "scene remove-component",
                "scene remove-component --path <Assets/...> --node <scenePath> --type <ComponentType> [--index N] --force",
                "Removes a component from a GameObject; shortcut for a single remove-component scene patch operation.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandScenePatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Always requires --force.", "Internally delegates to scene patch." },
                forceRule: ForceRule.Always),
            new CliCommandDescriptor(
                "scene assign-material",
                "scene assign-material --node <scenePath> --material <Assets/...>",
                "Assigns a material asset to MeshRenderer.sharedMaterials[0] on a node in the active loaded scene.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandSceneAssignMaterial,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Uses the active scene instead of --path.", "Saves the active scene immediately after assignment." }),
            new CliCommandDescriptor(
                "scene list-components",
                "scene list-components --node <scenePath>",
                "Lists all components on a GameObject in the active loaded scene, returning type names and indices.",
                CliCommandGroup.SceneWorkflows,
                ProtocolConstants.CommandSceneListComponents,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                notes: new[] { "Uses the active scene.", "Returns type + index pairs for use with add-component, remove-component, and modify-component." }),
            new CliCommandDescriptor(
                "prefab inspect",
                "prefab inspect --path <Assets/...> [--with-values] [--max-depth <N>] [--omit-defaults]",
                "Inspects prefab hierarchy and serialized property paths; use --with-values when authoring patch specs and the other options to reduce payload size.",
                CliCommandGroup.PrefabWorkflows,
                ProtocolConstants.CommandPrefabInspect,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                notes: new[] { "Use --with-values before authoring a patch spec.", "Use --max-depth and --omit-defaults to reduce inspect payload size.", "Output with --omit-defaults is read-only; omitted fields are not restored when used as patch input.", "Detailed prefab patch rules live in docs/prefab-spec.md." }),
            new CliCommandDescriptor(
                "prefab create",
                "prefab create --path <Assets/...> (--spec-file <file.json> | --spec-json <json>) [--force]",
                "Creates a prefab from a JSON structure spec; use --force to overwrite an existing asset.",
                CliCommandGroup.PrefabWorkflows,
                ProtocolConstants.CommandPrefabCreate,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Use this instead of asset create --type prefab for structured prefab authoring.", "Detailed prefab patch rules live in docs/prefab-spec.md." }),
            new CliCommandDescriptor(
                "prefab patch",
                "prefab patch --path <Assets/...> (--spec-file <file.json> | --spec-json <json>) [--force]",
                "Applies a deterministic patch spec to an existing prefab; destructive operations require --force.",
                CliCommandGroup.PrefabWorkflows,
                ProtocolConstants.CommandPrefabPatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Detailed prefab patch rules live in docs/prefab-spec.md." },
                forceRule: ForceRule.OnDestructiveOp),
            new CliCommandDescriptor(
                "prefab add-component",
                "prefab add-component --path <Assets/...> --node <nodePath> --type <ComponentType> [--values <json>]",
                "Adds a component to a prefab node; shortcut for a single add-component prefab patch operation.",
                CliCommandGroup.PrefabWorkflows,
                ProtocolConstants.CommandPrefabPatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Internally delegates to prefab patch." }),
            new CliCommandDescriptor(
                "prefab remove-component",
                "prefab remove-component --path <Assets/...> --node <nodePath> --type <ComponentType> [--index N] --force",
                "Removes a component from a prefab node; shortcut for a single remove-component prefab patch operation.",
                CliCommandGroup.PrefabWorkflows,
                ProtocolConstants.CommandPrefabPatch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Always requires --force.", "Internally delegates to prefab patch." },
                forceRule: ForceRule.Always),
            new CliCommandDescriptor(
                "prefab list-components",
                "prefab list-components --path <Assets/...> --node <nodePath>",
                "Lists all components on a node in a prefab asset, returning type names and indices.",
                CliCommandGroup.PrefabWorkflows,
                ProtocolConstants.CommandPrefabListComponents,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                notes: new[] { "Returns type + index pairs for use with prefab patch add-component/remove-component." }),
            new CliCommandDescriptor(
                "test list",
                "test list [--mode <edit|play|all>]",
                "Lists EditMode and/or PlayMode test cases discovered in the running editor.",
                CliCommandGroup.Diagnostics,
                ProtocolConstants.CommandTestList,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Returns full test names, assemblies, modes, and categories." }),
            new CliCommandDescriptor(
                "test run",
                "test run --mode <edit|play> [--filter <substring>] [--category <name>] [--assembly <name>] [--no-domain-reload] [--timeout <seconds>] [--wait]",
                "Executes EditMode or PlayMode tests. EditMode returns synchronously; PlayMode returns runId immediately and persists results to Library/com.yhc509.unity-cli-bridge/test-runs/<runId>.json.",
                CliCommandGroup.Diagnostics,
                ProtocolConstants.CommandTestRun,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[]
                {
                    "EditMode: synchronous response with full TestRunResult JSON.",
                    "PlayMode: asynchronous. STARTED+runId response; poll with `test results` or use --wait.",
                    "--no-domain-reload speeds up PlayMode runs but risks static state leakage (warning emitted in response).",
                    "Default timeout 300s, max 1800s. Timeout triggers TestRunnerApi.CancelTestRun(runGuid).",
                },
                forceRule: ForceRule.None,
                defaultLiveTimeoutMs: ProtocolConstants.DefaultTestRunTimeoutSeconds * 1000),
            new CliCommandDescriptor(
                "test results",
                "test results [--run-id <id>]",
                "Retrieves cached test run result (or in-progress status). Without --run-id, returns the last run.",
                CliCommandGroup.Diagnostics,
                ProtocolConstants.CommandTestResults,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                notes: new[] { "Reads from Library/com.yhc509.unity-cli-bridge/test-runs/<runId>.json or in-memory SessionState." }),
            new CliCommandDescriptor(
                "package list",
                "package list",
                "Lists all installed packages in the project.",
                CliCommandGroup.PackageManagement,
                ProtocolConstants.CommandPackageList,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                defaultLiveTimeoutMs: ProtocolConstants.DefaultPackageLiveTimeoutMs),
            new CliCommandDescriptor(
                "package add",
                "package add --name <package> [--version <version>]",
                "Adds a package to the project; supports registry, git URL, and local paths.",
                CliCommandGroup.PackageManagement,
                ProtocolConstants.CommandPackageAdd,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "패키지 작업 중 Editor가 일시 정지될 수 있습니다." },
                defaultLiveTimeoutMs: ProtocolConstants.DefaultPackageLiveTimeoutMs),
            new CliCommandDescriptor(
                "package remove",
                "package remove --name <package> --force",
                "Removes a package from the project; always requires --force.",
                CliCommandGroup.PackageManagement,
                ProtocolConstants.CommandPackageRemove,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "Removal is always gated by --force.", "패키지 작업 중 Editor가 일시 정지될 수 있습니다." },
                forceRule: ForceRule.Always,
                defaultLiveTimeoutMs: ProtocolConstants.DefaultPackageLiveTimeoutMs),
            new CliCommandDescriptor(
                "package search",
                "package search --query <text>",
                "Searches the Unity registry for packages matching the query.",
                CliCommandGroup.PackageManagement,
                ProtocolConstants.CommandPackageSearch,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                defaultLiveTimeoutMs: ProtocolConstants.DefaultPackageLiveTimeoutMs),
            new CliCommandDescriptor(
                "material info",
                "material info --path <Assets/...mat> [--omit-defaults]",
                "Inspects a material's shader and property values, with an option to omit properties still at the shader defaults.",
                CliCommandGroup.MaterialWorkflows,
                ProtocolConstants.CommandMaterialInfo,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: true,
                notes: new[] { "Use --omit-defaults to reduce payload size by omitting properties equal to the shader default." }),
            new CliCommandDescriptor(
                "material set",
                "material set --path <Assets/...mat> (--property <name> --value <val> | --texture <name> --asset <Assets/...>)",
                "Sets a material property value or texture.",
                CliCommandGroup.MaterialWorkflows,
                ProtocolConstants.CommandMaterialSet,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa click",
                "qa click (--qa-id <id> | --target <path>) [--button left|right]",
                "Clicks a UI element identified by QA ID or GameObject path; defaults to left click and supports right click with --button right; requires Play Mode.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaClick,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa tap",
                "qa tap (--x <int> --y <int> | --target <path>) [--button left|right] [--screenshot-width <int> --screenshot-height <int>]",
                "Taps at a screenshot-derived coordinate, or at a world object resolved by --target. Defaults to left click and supports right click with --button right. With --x/--y, pass screenshot image coordinates directly with a top-left origin; the bridge auto-uses the last captured screenshot size when available and handles Y-flip plus scaling internally (--screenshot-width/--screenshot-height override the source size). With --target, the bridge invokes the object's IQaTappable action when present, otherwise simulates an Input System tap at the object's anchor; requires Play Mode.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaTap,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa swipe",
                "qa swipe [--target <path>] --from <x,y> --to <x,y> [--duration <ms>] [--button left|right] [--screenshot-width <int> --screenshot-height <int>]",
                "Swipes over multiple frames; defaults to left button drag and supports right button drag with --button right. Without --target, --from/--to use screenshot-style top-origin coordinates and auto-scale from the last captured screenshot when available, while --target keeps them as pixel offsets from the target RectTransform center; pass --screenshot-width/--screenshot-height to override the source size; requires Play Mode.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaSwipe,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa key",
                "qa key --key <keyName>",
                "Simulates a key press via Input System; requires Play Mode.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaKey,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa ui-dump",
                "qa ui-dump [--screenshot-width <int> --screenshot-height <int>]",
                "Dumps currently clickable UI elements (path, type, text, interactable, image-space rect/center) as JSON; requires Play Mode. Feed a returned path to `qa click --target`, or centerX/centerY to `qa tap`. The returned path is reliable when unique; if same-named siblings share a path, tap by centerX/centerY instead. Coordinates use the last captured screenshot size unless overridden.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaUiDump,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa world-dump",
                "qa world-dump [--include-offscreen] [--screenshot-width <int> --screenshot-height <int>]",
                "Dumps non-UI world objects that opt in via IQaTappable/QaTappable (label, hierarchy path, image-space center, onScreen, hasAction) as JSON; requires Play Mode. Feed a returned path to `qa tap --target`. Off-screen objects are excluded unless --include-offscreen is set. Coordinates use the last captured screenshot size unless overridden.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaWorldDump,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa run-sequence",
                "qa run-sequence --spec-json <json|@file> [--timeout <ms>]",
                "Runs a linear sequence of condition-gated action steps in the bridge: each step waits (ANDed conditions) then executes its actions, with no per-step screenshot round-trip. Conditions read built-in state (active/gone/transform/scene/log/interactable) or game-exposed IQaQueryable values, compared with ==/!=/>=/<=/near/changed. Actions reuse qa key/tap/swipe/wait. Returns the completed step count, or the stopped step with its unmet conditions and a state snapshot on timeout. Requires Play Mode; no force-rule.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaRunSequence,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                defaultLiveTimeoutMs: ProtocolConstants.MaxQaRunSequenceTimeoutMs + 5_000),
            new CliCommandDescriptor(
                "qa wait",
                "qa wait --ms <int>",
                "Waits for the specified number of milliseconds (local only, does not contact the editor).",
                CliCommandGroup.QaWorkflows,
                protocolCommand: null,
                canUseLocal: true,
                canUseLive: false,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "qa wait-until",
                "qa wait-until (--scene <name> | --log-contains <text> | --object-exists <qa-id|path> | --object-interactable <qa-id|path> | --object-gone <qa-id|path>) [--timeout <ms>]",
                "Polls the editor until all supplied conditions are met (AND) or timeout expires; supports waiting for clickable/interactable UI or inactive/destroyed objects; requires Play Mode.",
                CliCommandGroup.QaWorkflows,
                ProtocolConstants.CommandQaWaitUntil,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "instances list",
                "instances list",
                "Lists known Unity project instances and the active registry selection.",
                CliCommandGroup.InstanceManagement,
                protocolCommand: null,
                canUseLocal: true,
                canUseLive: false,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "instances use",
                "instances use <projectHash|projectPath|projectName>",
                "Pins the active target project by hash, project path, or registered project name. Existing directory paths win over name matches.",
                CliCommandGroup.InstanceManagement,
                protocolCommand: null,
                canUseLocal: true,
                canUseLive: false,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "doctor",
                "doctor",
                "Shows registry, project detection, Unity path, and live reachability diagnostics.",
                CliCommandGroup.Diagnostics,
                protocolCommand: null,
                canUseLocal: true,
                canUseLive: false,
                isAllowedWhileBusy: false),
            new CliCommandDescriptor(
                "raw",
                "raw [--force] --json '{\"command\":\"status\",\"arguments\":{}}'",
                "Sends a raw live protocol envelope for low-level debugging.",
                CliCommandGroup.Diagnostics,
                protocolCommand: null,
                canUseLocal: false,
                canUseLive: true,
                isAllowedWhileBusy: false,
                notes: new[] { "This bypasses typed CLI validation.", "Use --force to inject `force: true` into raw arguments for destructive commands." }),
        };

        private static readonly string[] _supportedProtocolCommands = BuildSupportedProtocolCommands();

        public static CliCommandDescriptor[] GetCommands()
        {
            return (CliCommandDescriptor[])_commands.Clone();
        }

        public static string BuildHelpText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] <command> [options]");
            builder.AppendLine();
            builder.AppendLine("options:");
            builder.AppendLine("  --json                Equivalent to --output json. If both --json and --output are specified, the last one wins.");
            builder.AppendLine("  --output <mode>       Response format: default, json (full envelope), or compact (data payload / compact error JSON).");
            builder.AppendLine("  --project <path|name>  Existing directory paths take precedence over registered project names. Project-name matches are case-insensitive.");
            builder.AppendLine();
            builder.AppendLine("commands:");
            foreach (CliCommandDescriptor command in _commands)
            {
                builder.Append("  ");
                builder.AppendLine(command.Synopsis);
            }

            return builder.ToString();
        }

        public static string[] GetSupportedProtocolCommands()
        {
            return (string[])_supportedProtocolCommands.Clone();
        }

        private static string[] BuildSupportedProtocolCommands()
        {
            var commands = new List<string> { ProtocolConstants.CommandPing };
            foreach (CliCommandDescriptor descriptor in _commands)
            {
                if (descriptor.CanUseLive && !string.IsNullOrWhiteSpace(descriptor.ProtocolCommand))
                {
                    commands.Add(descriptor.ProtocolCommand!);
                }
            }

            return commands.ToArray();
        }

        public static bool IsCommandAllowedWhileBusy(string command)
        {
            if (string.Equals(command, ProtocolConstants.CommandPing, StringComparison.Ordinal))
            {
                return true;
            }

            CliCommandDescriptor? descriptor = FindByProtocolCommand(command);
            return descriptor is not null && descriptor.IsAllowedWhileBusy;
        }

        public static bool IsProtocolCommandInGroup(string command, CliCommandGroup group)
        {
            CliCommandDescriptor? descriptor = FindByProtocolCommand(command);
            return descriptor is not null && descriptor.Group == group;
        }

        public static CliCommandDescriptor? FindByCommand(string command)
        {
            foreach (CliCommandDescriptor descriptor in _commands)
            {
                if (string.Equals(descriptor.Command, command, StringComparison.Ordinal))
                {
                    return descriptor;
                }
            }

            return null;
        }

        public static CliCommandDescriptor? FindByProtocolCommand(string command)
        {
            foreach (CliCommandDescriptor descriptor in _commands)
            {
                if (!string.IsNullOrWhiteSpace(descriptor.ProtocolCommand)
                    && string.Equals(descriptor.ProtocolCommand, command, StringComparison.Ordinal))
                {
                    return descriptor;
                }
            }

            return null;
        }
    }
}
