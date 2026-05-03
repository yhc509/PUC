# Changelog

## [Unreleased]

### Security
- Added `Library/com.yhc509.unity-cli-bridge/backups/` backup/restore transactions for scene/prefab patch and asset overwrite flows, including `.meta` preservation and critical restore-failure reporting; backups stay outside `Assets/` and normal Unity git tracking.

### Added
- `BACKUP_FAILED`, `BACKUP_RESTORE_FAILED`, and `SCENE_DIRTY` protocol error constants for transactional mutation failures and dirty scene refusal.

### Changed
- `scene patch` now refuses already-loaded dirty target scenes even with `--force`; callers must save or discard in-memory scene changes before patching.
- Backup files are written under the project `Library/` folder instead of next to assets, avoiding AssetDatabase scanning and accidental git exposure.

### Notes
- Prefab Edit Mode dirty-state checks remain a follow-up item for a later PR.

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
