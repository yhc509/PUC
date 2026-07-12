# Changelog

## [Unreleased]

### Added
- Play Mode mp4 recording via `record start`, `record stop`, `record status`, and `qa run-sequence --record`.
- `qa ui-dump` and `qa world-dump` now accept token-trimming filters (`--limit`, `--text`, plus UI-only `--interactable-only` and `--omit-rect`) while preserving default output when omitted (#130).
- List-style responses now have opt-in trims: `read-console --no-stacktrace`, `test list --no-detail`, `test run/results --failures-only`, and `instances list --brief` (#131).

### Fixed
- `qa ui-dump` text extraction now stays within each clickable element's owned label subtree, preventing nested clickable controls from borrowing each other's labels.

### Compatibility
- Breaking: the minimum supported Unity version is now `2023.1`, and this package pins `com.unity.recorder` `5.1.6` for Play Mode recording.
- Older Recorder releases can fail to compile on Unity `6000.4` and newer because Unity promotes the obsolete object identity API to a compile-time error.

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
