# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.3.4] - 2026-06-26

### Added
- Play Mode mp4 recording via `record start`, `record stop`, `record status`, and `qa run-sequence --record`.
- `qa ui-dump` and `qa world-dump` now accept token-trimming filters (`--limit`, `--text`, plus UI-only `--interactable-only` and `--omit-rect`) while preserving default output when omitted (#130).
- List-style responses now have opt-in trims: `read-console --no-stacktrace`, `test list --no-detail`, `test run/results --failures-only`, and `instances list --brief` (#131).

### Compatibility
- This package now depends on `com.unity.recorder`; importing projects will pull it in.
- Recording declares `com.unity.recorder` `2.5.2` as the dependency floor, but the resolved Recorder version varies by Unity version. The feature has been verified with Unity 6 and Recorder `5.1.x`; Unity 2021.3 with Recorder `2.5.x` is a known limitation and has not yet been live-verified.

## [0.3.3] - 2026-06-21

### Added
- `scene inspect` and `prefab inspect` accept `--node <path>` to return a single node and its subtree instead of the entire hierarchy. For an agent that only needs one object, this cuts the response — and the tokens spent reading it — by roughly 70–90% on a targeted lookup, and removes the need to dump a full `--with-values` hierarchy before every patch. Omitting `--node` is unchanged. A bare `--node /` means the whole hierarchy, matching how scene paths already treat `/` as the virtual root (#126).
- `package list` accepts `--filter <substring>` (matches a package `name` or display name, case-insensitively) and `--limit <N>`. Checking whether a single package is installed no longer returns the entire dependency set — a targeted lookup drops the response by ~95%. Without these options the output, including sort order, is unchanged (#128).

### Changed
- The bundled AI-agent operator skill now defaults to lower-token command patterns — compact console reads, default-omitting inspects, and smaller agent-facing screenshots — so agent sessions spend fewer tokens out of the box (#125).

## [0.3.2] - 2026-06-21

### Added
- Play Mode mp4 recording via `record start`, `record stop`, `record status`, and `qa run-sequence --record`.
- `qa click`, `qa tap`, and `qa swipe` now accept `--button left|right` so Play Mode QA can drive right-click and right-drag input paths. The default remains `left`.

### Fixed
- Fixed #78: queued IPC commands that have not started yet are now cancelled when the CLI disconnects or times out, preventing delayed writes after the caller has already failed. Commands that have started still use at-least-once semantics, so check state before retrying mutation commands after a timeout.
- `scene set-transform` and `scene assign-material` now refuse to run when the active scene already has unsaved changes, instead of silently saving those unrelated edits along with the requested change. This matches the existing `scene patch` behavior; save or discard first.
- `prefab patch` and `prefab create` (overwrite) now refuse to write when the target prefab is open in a Prefab Stage with unsaved changes — including a parent stage left dirty while you edit a nested Prefab — instead of silently overwriting your in-editor edits. Save or discard the Prefab Stage first (#119).
- `qa ui-dump` label search is now scoped to each element's own subtree, so a label no longer matches text from an unrelated same-named sibling (#113).

### Security
- Asset path normalization now rejects path-traversal inputs (absolute or drive-qualified paths and `.`/`..` segments) and adds a canonical Assets/Packages containment check, so inputs like `Assets/../ProjectSettings/foo` can no longer escape the intended root (#69).
- Scene and prefab component patching now refuses serialized fields that are hidden from `inspect`, so the patchable surface matches what `inspect` shows instead of letting hand-written property paths reach internal or non-editable fields. `SerializeReference` (`$type`) assignments are validated against the field's declared type and must be a constructible, assignable type.

### Compatibility
- This package now depends on `com.unity.recorder`; importing projects will pull it in.
- Recording declares `com.unity.recorder` `2.5.2` as the dependency floor, but the resolved Recorder version varies by Unity version. The feature has been verified with Unity 6 and Recorder `5.1.x`; Unity 2021.3 with Recorder `2.5.x` is a known limitation and has not yet been live-verified.

## [0.3.1] - 2026-06-12

### Added
- `qa world-dump` lists non-UI world objects (3D/2D scene objects, not Canvas UI) that a game opts in to exposing — through the `IQaTappable` interface in code or the `QaTappable` marker component in the inspector. Each entry returns a `label`, hierarchy `path`, image-space center, an `onScreen` flag, and whether it has a tap action. Feed a `path` to `qa tap --target`, which runs the object's own `TryQaTap()` action first and otherwise falls back to an Input System pointer tap at the object's screen anchor — reaching raw Input System polling that coordinate-based `qa tap` and the EventSystem cannot. Off-screen objects are excluded unless `--include-offscreen` is passed. Play Mode is required.
- `qa run-sequence --spec-json <json|@file>` runs a condition-gated, multi-step action sequence in a single call, removing the screenshot → reason → act round-trip that makes deep turn-based and real-time flows tedious. Each step waits until its conditions hold — built-in checks (`active`, `gone`, `transform`, `scene`, `log`, `interactable`) or values the game exposes through the new `IQaQueryable` interface — then runs `key`/`tap`/`swipe`/`wait`/`screenshot` actions. Conditions within a step are ANDed; comparison operators are `==`, `!=`, `>=`, `<=`, `near` (with epsilon), and `changed`. On timeout the response reports which step's conditions went unmet plus a state snapshot for diagnosis. Play Mode is required.

### Fixed
- The **Unity CLI Manager** window (`Window > Unity CLI Manager`) now installs and updates against the latest *published* GitHub release instead of building the download URL from the package version. Previously, when the matching release was still a draft, the installer tried to fetch a draft asset and failed with an error dialog. Draft and pre-release versions are now ignored.
- The installer now tells a failed release check (network error, timeout, GitHub rate limit) apart from "no published release available". A transient failure no longer locks the window into a false "no release" state for up to an hour — it surfaces a retryable "Check failed" status and keeps using the cached release version. An installed CLI whose version is unknown is once again offered a reinstall path.

## [0.3.0] - 2026-06-10

### Added
- `qa ui-dump` lists the currently clickable UI elements as JSON — each with its hierarchy path, component type, child text, interactable state, and image-space rect/center — so an agent can resolve a target in one call instead of capturing a screenshot, analyzing the image, and guessing coordinates. Feed a returned `path` to `qa click --target`, or `centerX`/`centerY` to `qa tap`. Clickable elements are detected via `IPointerClickHandler` and restricted to RectTransform-backed UI; text and interactable state are read by reflection, so the package gains no dependency on `UnityEngine.UI` or TextMeshPro. Interactability honors the `CanvasGroup` chain via `Selectable.IsInteractable()`.
- `screenshot --format <png|jpg>`, `--quality <1-100>`, and `--max-width <int>`. JPEG output and aspect-preserving downscaling make agent-consumed captures much smaller (PNG remains the default). The response reports the `format` written alongside the existing `fileSizeBytes`.
- `qa wait-until --object-interactable <id|path>` waits until a target is active and actually interactable (honoring the `CanvasGroup` chain), and `qa wait-until --object-gone <id|path>` waits until a target disappears from the active hierarchy. These replace fixed sleeps with condition polling. Multiple conditions combine with AND; a self-contradictory combination on the same target is rejected up front.

### Fixed
- `qa tap` / `qa swipe` coordinate conversion now samples the Game View render resolution (`Handles.GetMainGameViewSize()`) instead of the editor panel size, so taps no longer miss their target in Play Mode. Screenshot responses report the same render resolution in `screenWidth`/`screenHeight`.
- When no screenshot has been captured and no explicit size is given, `qa tap` / `qa swipe` / `qa ui-dump` now fall back to the current Game View size as the coordinate basis instead of skipping the Y-flip, so coordinates stay top-left and round-trip even on the first call.

## [0.2.3] - 2026-06-05

### Added
- `execute` now returns structured values: assign to `__pucResult` to receive a type-preserving JSON result (float G9 / double G17 round-trip). `ExecuteValueSerializer` is public for use in `custom` commands.

### Changed
- No-`--project` routing is now safe in multi-instance setups. When two or more Editors are live and none is pinned, the CLI fails with a candidate list instead of silently selecting the first instance. Routing still auto-selects when the current directory is inside a project or a pin is set. A pin set via `instances use` is distinguished from the auto-promoted `activeProjectRoot` and is the only value trusted for the no-`--project` fallback.

### Compatibility
- `activeProjectRootPinned` is an additive registry field that older CLIs/packages ignore; no wire/protocol bump. Pin persistence requires both the CLI and the Unity package to be on this version — in a mixed setup an older Editor-side bridge drops the pin on each heartbeat, after which routing falls back to the safe ambiguous-target error (never a silent mis-route).
- Behavior change: multi-instance commands with no `--project` previously succeeded by picking the first instance; they now fail with a candidate list. Pass `--project`, run from a project directory, or set a default with `instances use`.

## [0.2.2] - 2026-05-29

### Added
- `unity-cli compile --wait` and `unity-cli refresh --wait` flags that block until the Editor finishes compiling, importing, and the bridge is reachable again, so callers don't have to poll status manually.

## [0.2.1] - 2026-05-28

### Changed
- BUSY error responses now include retry and diagnostic guidance instead of encouraging callers to spawn a new Editor.

## [0.2.0] - 2026-05-16

### Added
- `test list`, `test run`, and `test results` commands for running Unity Test Runner suites from the CLI. EditMode returns synchronously; PlayMode returns `STARTED` plus a `runId` immediately, then writes results atomically to `Library/com.yhc509.unity-cli-bridge/test-runs/<runId>.json`. Agents can use `--wait` to poll from the CLI and build a repair loop around failing tests.
- `test run --mode play --no-domain-reload` as an opt-in speed path that can save 30-120 seconds of domain reload overhead when the suite is safe from static state leakage; responses warn about the risk.

### Changed
- The legacy `run-tests` parser path is no longer present. Use `test run` for the structured Test Runner workflow.

### Fixed
- `test run --filter` now matches test full names by case-insensitive substring instead of passing the substring to Unity Test Framework as an exact full-name match.
- PlayMode test runs now keep a watchdog after the initial `STARTED` response, cancel and mark timed-out runs after the configured timeout plus grace period, and restore callback registration after PlayMode domain reloads so results are flushed instead of leaving the single-flight lock stuck.
- `test run --mode play` now fails with `TEST_PLAYMODE_ENTRY_FAILED` if Unity does not begin entering Play Mode shortly after `TestRunnerApi.Execute`.
- Non-`Completed` test run results now return an error envelope and CLI exit code 1; interrupted EditMode runs after domain reload are marked `Failed` instead of leaving the run lock stuck.
- `test run --mode edit --no-domain-reload` is now rejected as a usage error because the option only applies to PlayMode.

## [0.1.13] - 2026-05-13

### Added
- `unity-cli execute --timeout <초>` cooperative cancellation token. Wrapper exposes `__pucToken` for user code to check; timeouts surface as `EXECUTE_TIMEOUT` errors. Default 30s, max 600s. Non-cooperative code still occupies the Editor main thread (force-user responsibility).
- `unity-cli doctor` now reports the live-side error code and message when it cannot reach the Editor, so protocol mismatches (`PROTOCOL_MISMATCH`) are distinguishable from a simple unreachable instance.

### Changed
- `instances list`/`status` responses now use `activeProjectRoot` (canonical project root) instead of `activeProjectHash`. Instances stay separated safely in multi-worktree setups even if their 12-character hashes collide. Hash-only target resolution rejects an unsuffixed 12-character hash as ambiguous when a suffixed sibling exists, so accidental routing to the wrong Editor is no longer silent.

### Fixed
- Windows named-pipe listener now takes an OS-level ownership lock per pipe name, preventing two Editors that share the same 12-character hash from both binding the same pipe through a probe race.
- Unix socket cleanup probes the path for a live listener before unlinking, so a freshly bound socket from another Editor is no longer accidentally deleted by a late teardown from the previous owner. Stale suffixed socket files (`hash-1`...`hash-15`) are also reclaimed at acquire time.
- `UnixSocketProbe.IsLive` now uses a 50 ms connect timeout, matching the named-pipe probe and avoiding a startup stall when an old socket file points at a process that no longer accepts connections.

### Compatibility
- Wire protocol bumped from 3 to 4 for the registry identity migration. Upgrade the CLI binary and Unity package together; mixed versions return `PROTOCOL_MISMATCH`. Existing `registry.json` files with `activeProjectHash` are migrated in memory on first load and persisted on the next registry write.

## [0.1.12] - 2026-05-08

### Changed
- Live IPC error responses now carry `error.details` as an inline JSON value (object, string, or null), mirroring the `data` field. CLI `--json` output preserves structured error context directly instead of wrapping it in an escaped JSON string.
- Bumped GitHub Actions workflows to versions running on Node 24 (`actions/checkout@v5`, `actions/setup-dotnet@v5`, `actions/upload-artifact@v6`, `actions/download-artifact@v7`, `softprops/action-gh-release@v3`) ahead of the GitHub Actions Node 20 sunset.

### Compatibility
- Upgrade the CLI binary and Unity package together. The wire `protocolVersion` is bumped to `3` for the inline `error.details` change; mixing versions returns a clear `PROTOCOL_MISMATCH` error pointing to whichever side is out of date.

## [0.1.11] - 2026-05-07

### Added
- Live IPC now reports a clear, side-specific error message when the CLI and Unity package wire versions are incompatible, telling you which side to upgrade instead of returning empty or malformed responses.

### Changed
- Live IPC responses now send payloads as an inline JSON `data` field, so CLI `--json` output preserves structured objects directly instead of wrapping them in a string-valued `dataJson` field.
- JSON output paths (`--output json` and outbound IPC payloads) now emit non-ASCII text (such as Korean) literally instead of escaping it as `\uXXXX`. Existing recipients keep working because `\uXXXX` and literal forms are both valid JSON.

### Fixed
- Inspector and patch responses with `NaN` or `±Infinity` float values now serialize as JSON strings instead of breaking JSON parsing on the CLI side.
- The bridge falls back to a structured `INTERNAL_INVALID_PAYLOAD` error envelope when a handler somehow produces invalid raw JSON, instead of dropping the connection.

### Removed
- Removed the `ResponseEnvelope.dataJson` wire field from live IPC responses.

### Compatibility
- Upgrade the CLI binary and Unity package together. Mixing versions returns a clear protocol-mismatch error pointing to whichever side is out of date, but commands cannot run until both sides match.

## [0.1.10] - 2026-05-07

### Added
- CLI protocol builds now fail if shared protocol source files are added directly under `cli/UnityCli.Protocol/`; protocol sources must live in the Unity package's shared `Runtime/Protocol/` directory.

### Fixed
- Asset create provider registry no longer gets stuck in a partial-init state when a built-in provider registration throws.
- Bridge now returns a structured `INVALID_COMMAND` response when an IPC payload fails JSON deserialization, instead of dropping the connection.
- CLI installer's archive-extract / chmod step no longer risks a pipe-buffer deadlock when the spawned process produces non-trivial stdout or stderr.
- CLI instance registry treats records with unparseable `lastSeenUtc` as stale, subject to live-PID confirmation, instead of keeping them alive forever.
- Protocol JSON handling now explicitly uses `MaxDepth = 128`, avoiding the System.Text.Json default depth of 64 that could make deep scene inspect data fail as silent `data = null`.
- `scene patch` / `prefab patch` `Bounds` mutations with missing `center` or `size` now return a clear `PREFAB_FIELD_INVALID` error instead of crashing with a null reference exception.

## [0.1.9] - 2026-05-03

### Security
- Enforce server-side `--force` gates on destructive or dangerous commands, including asset delete/move/rename, scene and prefab patch destructive operations, execute-code, and package remove. CLI-only validation could be bypassed through raw IPC.
- Add `Library/com.yhc509.unity-cli-bridge/backups/` backup/restore transactions for scene/prefab patch and asset overwrite flows, including `.meta` preservation and critical restore-failure reporting; backups stay outside `Assets/` and normal Unity git tracking.

### Added
- Catalog `ForceRule` metadata as the single source of truth for force-gated commands.
- `raw --force` support to inject `force=true` into raw envelope arguments.
- `BACKUP_FAILED`, `BACKUP_RESTORE_FAILED`, `SCENE_DIRTY`, and `PACKAGE_TIMEOUT` protocol error constants for transactional mutation failures, dirty scene refusal, and stalled Package Manager requests.

### Changed
- `raw --force` now fails fast when the raw payload explicitly conflicts with the flag instead of overwriting payload `force`.
- `scene patch` now refuses already-loaded dirty target scenes even with `--force`; callers must save or discard in-memory scene changes before patching.
- Backup files are written under the project `Library/` folder instead of next to assets, avoiding AssetDatabase scanning and accidental git exposure.

### Fixed
- `package list`, `package add`, `package remove`, and `package search` now poll Unity Package Manager requests from `EditorApplication.update` instead of blocking the editor thread, preserving bridge heartbeats and returning `PACKAGE_TIMEOUT` after 300 seconds if Package Manager stalls.
- Instance registry lock now uses atomic `FileMode.CreateNew` ownership with PID + UTC timestamp content, releases the lock only when this process actually acquired it, and recovers crash-leftover lock files through an open-then-rename-then-delete reclaim path that compares `Process.StartTime` against the recorded lock timestamp. Earlier behaviour could unlink a peer's live lock during a retry and could leave the bridge unable to register itself after a crash.

### Notes
- Prefab Edit Mode dirty-state checks remain a follow-up item for a later PR.

### Compatibility Note
- Destructive command protocol arguments now include a `force` field. Older clients that omit it are interpreted by the bridge as `force=false`, so destructive or dangerous operations are rejected until the client sends `force=true`.

## [0.1.8] - 2026-04-30

### Changed
- Renamed package and internal identifiers (#34). C# namespaces are now under `UnityCliBridge.Bridge.*`, the UPM package id is `com.yhc509.unity-cli-bridge`, the solution file is `UnityCliBridge.sln`, EditorPrefs use the `UnityCliBridge.CLI.*` prefix, the install directory is `~/.unity-cli-bridge`, and the GitHub URL changed.
- Moved the Editor menu entry to `Window > Unity CLI Manager` to match Unity's standard menu location (#34).
- Hardened `execute --args` wrapper compilation by isolating internal wrapper variables behind the reserved `__puc_internal_*` prefix, reporting user-code compile errors from `user-code`, and disabling debug/temp file retention for CodeDOM.

### Notes
- `execute --args` values should not contain secrets or credentials because CodeDOM may briefly create temporary `.cs` files under the OS temp directory; debug/temp retention settings reduce retained artifacts but do not fully prevent transient compiler files.
- The install directory changed; previous install locations from earlier versions are not migrated. Remove any old install manually and reinstall the CLI through `Window > Unity CLI Manager`.

## [0.1.7] - 2026-04-28

### Added
- Friendly key alias catalog for Rigidbody, Collider, Renderer, Light, and Camera component value patches. Aliases like `damping`, `materials[0]`, `shadowStrength`, `fieldOfView`, and `backgroundColor` now resolve to Unity's internal `SerializedProperty.propertyPath` values; multi-candidate aliases (e.g. `damping → m_Drag` on Unity 2021.3 and `m_LinearDamping` on Unity 6) are tried in order before falling back to the original key and `m_PascalCase` (#29).

### Fixed
- Reset Game View screenshot dimensions on Play Mode transitions to prevent stale coordinate scaling when Enter Play Mode Options disables domain reload (#28).

## [0.1.6] - 2026-04-28

### Added
- `execute` now accepts `--args <json>` for passing structured JSON into inline or file-based code execution.
- Execute wrapper code now exposes the supplied JSON as the `__pucArgsJson` string variable.

### Notes
- 이 기능은 bridge `0.1.6+` 필요. 구 bridge에 신규 CLI를 사용하면 `__pucArgsJson` 참조 시 컴파일 에러가 발생합니다.

## [0.1.5] - 2026-04-26

### Added
- `screenshot` response now includes 4 metadata fields — `screenWidth`, `screenHeight`, `coordinateOrigin`, `imageOrigin` — so AI agents can determine the `qa tap` coordinate system from the screenshot response alone (#21).

### Changed
- AI skills installer in `Window > Unity CLI Manager` now writes to the user's global skills directory (`~/.claude/skills/`, `~/.codex/skills/`) instead of `{ProjectRoot}/.claude/skills/` and `{ProjectRoot}/.agents/skills/`; the project-local `.claude/skills/unity-cli-operator` symlink was removed (#23).
- Rewrote the `unity-cli-operator` skill so agents stop bypassing it and detouring through `Unity -batchmode` headless runs or searching for a (nonexistent) Unity MCP server: `SKILL.md` description rewritten as a forward-firing trigger and a "진입 규칙" section added; `agents/openai.yaml` populated with `short_description` and `default_prompt` rules (#24).
- Bridge package author changed from `yhjang` to `yhc509` (#22).

## [0.1.4] - 2026-04-08

### Added
- QA screenshot coordinate conversion: `qa tap` and coordinate-based `qa swipe` now accept top-origin screenshot-space coordinates with automatic Y-axis inversion and resolution scaling when screenshot dimensions are available.
- `--screenshot-width` / `--screenshot-height` options for `qa tap` and `qa swipe` to explicitly specify screenshot resolution for coordinate scaling.
- Auto screenshot dimension fallback: bridge stores last Game View screenshot resolution and uses it automatically for subsequent QA coordinate conversion.
- macOS ad-hoc codesigning (`codesign -s -`) added to `publish-osx-arm64.sh`.

### Changed
- `qa tap` and coordinate-based `qa swipe` now convert coordinates from screenshot space (Y=0 at top) to Unity screen space (Y=0 at bottom) when screenshot dimensions are known (explicit or auto-detected). Without screenshot dimensions, coordinates pass through as raw screen pixels (backward-compatible).
- Improved error message for unsupported SerializedPropertyType to include the actual type name

### Documentation
- ExposedReference and FixedBufferSize are intentionally unsupported (extremely rare in typical scene/prefab workflows)

## [0.1.2] - 2026-04-07

### Added
- Component operations for scenes and prefabs: list, add, and remove components from the CLI (`scene list-components`, `scene add-component`, `scene remove-component`, `prefab list-components`, `prefab add-component`, and `prefab remove-component`) (#6).
- An AI skill installer in `Window > Unity CLI Manager` for Claude Code and Codex (#8).
- Latest GitHub release version display in `Window > Unity CLI Manager` to simplify update checks (#9).
- `SerializedValueApplier` support for `AnimationCurve` (with `preWrapMode`/`postWrapMode`), `Gradient`, `ManagedReference` (`[SerializeReference]`), and `Hash128` property types — enabling inspect/patch for virtually all Unity built-in components.

### Changed
- **Breaking:** Unified `--target` to `--node` in scene/prefab `add-component` and `remove-component` commands for consistency with other node-targeting commands.
- Split `InspectorUtility` into focused utility classes (`InspectorJsonWriterUtility`, `InspectorPathParserUtility`, `InspectorMutationReaderUtility`, `InspectorDefaultPruningUtility`) for maintainability.
- Reduced GC allocations across bridge handlers: single-parse `argumentsJson`, `ComponentEntry` struct conversion, cached protocol commands and asset descriptors, closure and LINQ elimination.
- Moved `Socket.Bind`/`Listen` to a background thread in `BridgeHost` to avoid editor startup hitch.

### Fixed
- A null check after `AddComponent` to prevent a null reference exception during component creation (#7).
- CLI Manager now preserves the last known release version on network failure instead of clearing the cache.
- Added `UnityWebRequest` timeouts (15 s for version checks, 60 s for downloads).
- Cached `IsUpdateAvailable()` result to avoid repeated version parsing in `OnGUI`.

## [0.1.1] - 2026-04-05

### Added
- `CLI Manager`, an EditorWindow for one-click `unity-cli` install and update from the Unity Editor (#5).
- CI status checks for pull requests and pushes to `main`.

### Changed
- Renamed the CLI binary from `puc` to `unity-cli` (#5).

## [0.1.0] - 2026-04-05

### Added
- Initial release of the UnityCliBridge mono-repo with a .NET 9 CLI, a Unity UPM package, and shared protocol models.
- Live IPC control for a running Unity Editor over local transports, with no manual bridge startup required.
- Scene and prefab inspect and patch workflows for structured hierarchy edits and serialized value changes.
- Asset workflows for search, metadata inspection, creation, move, rename, and delete operations.
- Commands for screenshots, materials, packages, custom execution, and Play Mode QA automation.
- Token-saving output modes for AI workflows, including `--output compact`, `--max-depth`, and `--omit-defaults`.
- Cross-platform distribution for macOS arm64 and Windows x64, plus a GitHub Actions release workflow (#3).

### Changed
- Rebranded the project from `PUC` to `UnityCliBridge`.

### Fixed
- Prevented the Unity "modified externally" dialog after scene and prefab saves (#1).
- Preserved missing `.meta` files during relevant asset operations (#2).
- Improved Windows pipe reconnect behavior and reduced instance registry file contention (#4).

### Removed
- Removed batch mode support; the CLI is now live IPC only and requires a running Unity Editor.
