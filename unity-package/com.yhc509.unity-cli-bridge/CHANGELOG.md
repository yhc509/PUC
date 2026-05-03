# Changelog

## [Unreleased]

### Fixed
- Asset create provider registry no longer gets stuck in a partial-init state when a built-in provider registration throws.
- Bridge now returns a structured `INVALID_COMMAND` response when an IPC payload fails JSON deserialization, instead of dropping the connection.
- CLI installer's archive-extract / chmod step no longer risks a pipe-buffer deadlock when the spawned process produces non-trivial stdout or stderr.
- CLI instance registry treats records with unparseable `lastSeenUtc` as stale, subject to live-PID confirmation, instead of keeping them alive forever.
- `scene patch` / `prefab patch` `Bounds` mutations with missing `center` or `size` now return a clear `PREFAB_FIELD_INVALID` error instead of crashing with a null reference exception.

## [0.1.9] - 2026-05-03

### Security
- Bridge now enforces server-side `--force` gates on destructive or dangerous commands, including asset delete/move/rename, scene and prefab patch destructive operations, execute-code, and package remove. Earlier releases relied on CLI-only validation that could be bypassed through raw IPC.
- Added `Library/com.yhc509.unity-cli-bridge/backups/` backup/restore transactions for scene/prefab patch and asset overwrite flows, including `.meta` preservation and critical restore-failure reporting; backups stay outside `Assets/` and normal Unity git tracking.

### Added
- `BACKUP_FAILED`, `BACKUP_RESTORE_FAILED`, `SCENE_DIRTY`, `PACKAGE_TIMEOUT`, and `PACKAGE_BUSY` protocol error constants for transactional mutation failures, dirty scene refusal, stalled Package Manager requests, and concurrent Package Manager request refusal.

### Changed
- `scene patch` now refuses already-loaded dirty target scenes even with `--force`; callers must save or discard in-memory scene changes before patching.
- Backup files are written under the project `Library/` folder instead of next to assets, avoiding AssetDatabase scanning and accidental git exposure.
- CLI package commands now default to a 360-second live timeout, leaving room for the bridge's 300-second Package Manager timeout response.

### Fixed
- `package list`, `package add`, `package remove`, and `package search` now poll Unity Package Manager requests from `EditorApplication.update` instead of blocking the editor thread, preserving bridge heartbeats and returning `PACKAGE_TIMEOUT` after 300 seconds if Package Manager stalls.
- Concurrent package commands now return `PACKAGE_BUSY` immediately instead of issuing overlapping Unity Package Manager `Client` requests.
- Instance registry lock now uses atomic `FileMode.CreateNew` ownership with PID + UTC timestamp content, releases the lock only when this process actually acquired it, and recovers crash-leftover lock files through an open-then-rename-then-delete reclaim path that compares `Process.StartTime` against the recorded lock timestamp. Earlier behaviour could unlink a peer's live lock during a retry and could leave the bridge unable to register itself after a crash.

### Notes
- Prefab Edit Mode dirty-state checks remain a follow-up item for a later PR.

### Compatibility Note
- Destructive command protocol arguments now include a `force` field. Older CLI clients that omit it are interpreted by the bridge as `force=false`, so destructive or dangerous operations are rejected until the client sends `force=true`.

## [0.1.8] - 2026-04-30

### Changed
- Renamed package id and internal identifiers (#34). Package id is now `com.yhc509.unity-cli-bridge`; namespaces are `UnityCliBridge.Bridge.*`; EditorPrefs use the `UnityCliBridge.CLI.*` prefix; the install directory is `~/.unity-cli-bridge`.
- Moved the Editor menu entry to `Window > Unity CLI Manager`.
- Hardened `execute-code` wrapper compilation by isolating internal wrapper variables behind the reserved `__puc_internal_*` prefix, reporting user-code compile errors from `user-code`, and disabling debug/temp file retention for CodeDOM.

### Notes
- `execute --args` values should not contain secrets or credentials because CodeDOM may briefly create temporary `.cs` files under the OS temp directory; debug/temp retention settings reduce retained artifacts but do not fully prevent transient compiler files.
- The install directory changed; previous install locations from earlier versions are not migrated. Remove any old install manually and reinstall the CLI from `Window > Unity CLI Manager`.

## [0.1.7] - 2026-04-28

### Added
- Friendly key alias catalog for Rigidbody, Collider, Renderer, Light, and Camera component value patches. Aliases resolve to Unity's `SerializedProperty.propertyPath`; multi-candidate aliases (e.g. `damping → m_Drag` on Unity 2021.3 and `m_LinearDamping` on Unity 6) are tried in order before falling back to the original key and `m_PascalCase`.

### Fixed
- Reset Game View screenshot dimensions on Play Mode transitions to prevent stale coordinate scaling when Enter Play Mode Options disables domain reload.

## [0.1.6] - 2026-04-28

### Added
- `execute-code` wrapper now exposes the JSON passed via the CLI's `--args` option as the `__pucArgsJson` string variable.

## [0.1.5] - 2026-04-26

### Added
- `screenshot` response now includes 4 metadata fields — `screenWidth`, `screenHeight`, `coordinateOrigin`, `imageOrigin` — so callers can derive the `qa tap` coordinate system from a single screenshot response.

### Changed
- AI skills installer in `Window > Unity CLI Manager` writes to the user's global skills directory (`~/.claude/skills/`, `~/.codex/skills/`) instead of the project root.
- Bundled `unity-cli-operator` skill rewritten to actively trigger on Unity tasks and explicitly close off `Unity -batchmode`/MCP detour paths.
- Package author changed from `yhjang` to `yhc509`.

## [0.1.4] - 2026-04-08

### Changed
- Improved error message for unsupported SerializedPropertyType to include the actual type name

### Documentation
- ExposedReference and FixedBufferSize are intentionally unsupported (extremely rare in typical scene/prefab workflows)

## [0.1.0] - 2026-03-17

- Initial package release
- Added local IPC bridge auto-start and instance registry integration
- Added live editor control for status, refresh, play state, menu execution, and console reads
- Added batch command runner support for editor-off automation
- Added asset query, mutation, and common asset creation support
- Added prefab inspect, create, and patch commands for hierarchy edits and serialized field updates
- Added editor-local `Newtonsoft.Json.dll` dependency for prefab spec parsing
