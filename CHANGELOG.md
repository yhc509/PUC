# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- CLI protocol builds now fail if shared protocol source files are added directly under `cli/UnityCli.Protocol/`; protocol sources must live in the Unity package's shared `Runtime/Protocol/` directory.

### Fixed
- Asset create provider registry no longer gets stuck in a partial-init state when a built-in provider registration throws.
- Bridge now returns a structured `INVALID_COMMAND` response when an IPC payload fails JSON deserialization, instead of dropping the connection.
- CLI installer's archive-extract / chmod step no longer risks a pipe-buffer deadlock when the spawned process produces non-trivial stdout or stderr.
- CLI instance registry treats records with unparseable `lastSeenUtc` as stale, subject to live-PID confirmation, instead of keeping them alive forever.
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
