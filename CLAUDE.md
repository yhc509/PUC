# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity CLI Bridge controls the Unity Editor from the command line without manual server startup. This mono-repo contains a .NET 9 CLI, the `com.yhc509.unity-cli-bridge` Unity UPM package, and shared protocol models.

The CLI is **live IPC only**. Unity commands require a running Editor with the bridge active.

## Build & Test Commands

```bash
# Build
dotnet build UnityCliBridge.sln -c Debug

# Run all tests
dotnet test UnityCliBridge.sln

# Run a single test
dotnet test UnityCliBridge.sln --filter "FullyQualifiedName~ClassName.MethodName"

# Publish macOS arm64 binary
./scripts/publish-osx-arm64.sh    # → dist/unity-cli/unity-cli

# Doc generation (verify docs match code)
dotnet run --project cli/UnityCli.DocGen -- --check

# Doc generation (write/update docs)
dotnet run --project cli/UnityCli.DocGen -- --write
```

## Architecture

The repo is a single solution (`UnityCliBridge.sln`) split across four projects: the CLI executable, a shared protocol library, a doc-gen tool, and xUnit tests. The directory tree follows standard .NET / UPM conventions — use `ls` / `find` for an exhaustive file list. The notes below cover the non-obvious entry points and how the pieces fit together.

**CLI (`cli/UnityCli.Cli/`)** — `.NET 9`, published self-contained for `osx-arm64` and `win-x64`. `CliApp.RunAsync` is the dispatcher: local-only flows (`status`, `instances`, `doctor`) are answered without IPC; everything else goes through `Services/CliArgumentParser` → `Models/ParsedCommand` → `Services/LocalIpcClient` to the running Editor. `Services/InstanceRegistryStore` reads `InstanceRegistryFile` (see Protocol below) to find the right Editor for a given project root.

**Shared protocol (`cli/UnityCli.Protocol/` ↔ `unity-package/.../Runtime/Protocol/`)** — The `.csproj` uses `<Compile Include>` links to compile the same `.cs` files from the Unity package. **A change to any protocol file is a change to both sides; keep them buildable for both `.NET 9` and Unity's runtime.** Shared protocol source files must live in `unity-package/com.yhc509.unity-cli-bridge/Runtime/Protocol/`; the CLI project enforces this via a build-time guard. Hot spots:
- `CliCommandCatalog.cs` is the single source of truth for command metadata, including `ForceRule` (None / OnOverwrite / OnDestructiveOp / Always) — every force-gating decision must trace back here.
- `FileBackupTransaction.cs` is `.NET`-testable and powers all backup/restore flows.
- `InstanceRegistryFile.cs` owns the atomic registry-lock protocol (atomic `FileMode.CreateNew`, PID + UTC timestamp content, stale-reclaim via open-then-rename-then-delete) used by both `BridgeHost` and the CLI's `InstanceRegistryStore`. It also owns per-instance 0600 auth-token sidecars (`<registryDir>/tokens/<hash>.token`) so mixed-version registry rewrites cannot strip live tokens.

**Bridge runtime (`unity-package/com.yhc509.unity-cli-bridge/Editor/`)** — Hosted in the Unity Editor.
- `BridgeHost.cs` is the bootstrap and dispatcher: it registers the project in the instance registry, starts the IPC listener (Named Pipe on Windows / Unix socket on macOS+Linux), and routes commands to one of the `*CommandHandler` classes (`Asset`, `AssetCreate`, `Scene`, `Prefab`, `Material`, `Package`, `Qa`, `Record`, `Screenshot`, `ExecuteCode`, `Custom`).
- `ClientDisconnectMonitor.cs` is the `.NET`-testable stream watcher that lets `BridgeHost` cancel queued requests when the CLI connection closes before dispatch.
- Scene/prefab patch logic is deliberately split across `SceneCommandHandler.Patching.cs` and `PrefabCommandHandler.Patching.cs` (partial classes) so the entry-point file stays small and the op-application code lives next to its inspector.
- `SerializedValueApplier.cs` (+ `.ComplexTypes.cs` partial) is the most fragile layer — it translates JSON values into `SerializedProperty.propertyPath` mutations with friendly-key fallback. Run `*-inspect --with-values` before patching to verify paths.
- `ExecuteValueSerializer.cs` serializes values returned from `execute` and custom commands with a safe whitelist and round-trip float/double formatting.
- `AssetBackupTransaction.cs` wraps `FileBackupTransaction` with an `AssetDatabase.Refresh` discipline (always in `finally`) and is the rollback core for scene patch / prefab patch / asset overwrite. Backups land in `Library/com.yhc509.unity-cli-bridge/backups/` to stay outside `Assets/` and `AssetDatabase` scanning.
- `PackageCommandHandler.cs` polls Unity Package Manager from `EditorApplication.update` (deferred dispatch) with a single-flight guard and a 300 s `PACKAGE_TIMEOUT` — never re-introduce blocking polls here.
- `RecordCommandHandler.cs` starts/stops Unity Recorder mp4 capture for Play Mode and writes finalized result sidecars under `Library/com.yhc509.unity-cli-bridge/recordings/`.
- `TestCommandHandler.cs` + `.EditMode.cs` + `.PlayMode.cs` (partial)와 `TestRunnerCallbacks.cs` (ScriptableObject)는 `TestRunnerApi`를 래핑한다. EditMode는 동기 응답, PlayMode는 비동기(즉시 `STARTED+runId`, 결과는 `Library/com.yhc509.unity-cli-bridge/test-runs/<runId>.json` atomic write). `DomainReloadDisableScope`는 `--no-domain-reload` 옵션을 구현하지만 디폴트는 Unity 정상 동작.
- `CliInstallerWindow.cs` / `CliInstallerState.cs` / `CliDownloader.cs` / `SkillInstaller.cs` form the `Window > Unity CLI Manager` flow that fetches the matching CLI binary from GitHub Releases and installs the AI-agent skill.

Tests live in `tests/UnityCli.Cli.Tests/` (xUnit, `.NET`-testable surface only). Editor-mode tests for in-Editor handlers live in the package's `Tests~/` folder.

## Key Conventions

- **Nullable references enabled** throughout (`#nullable enable`, implicit usings).
- **IPC request semantics:** queued requests are best-effort cancelled if the client disconnects before dispatch, but once a command starts running it cannot be interrupted. Mutation commands are therefore at-least-once: after a CLI timeout, check Editor/project state before retrying.
- **Asset paths** always use `Assets/...` format.
- **Destructive/dangerous ops require `--force`:** `asset delete` (always), `asset move/rename/create` (when overwriting), destructive scene/prefab patches, scene/prefab `remove-component`, `package remove`, and `execute`.
- **`execute` cooperative timeout:** `--timeout <초>` (default 30, max 600) 협력적 cancel. 사용자 코드가 `__pucToken` 체크해야 강제 종료. 비협조 코드는 main thread 점유 — force 사용자 책임.
- **`execute` structured results:** user code can assign to `__pucResult`; responses use `hasResult` plus a raw JSON `result` string from `ExecuteValueSerializer` (float G9 / double G17). Custom commands can call the same serializer directly.
- **Patch/overwrite rollback:** Scene/prefab patch and asset overwrite flows use `Library/com.yhc509.unity-cli-bridge/backups/` backups for the asset body and `.meta`; restore failures return backup paths for manual recovery.
- **Dirty scene patch refusal:** `scene patch` refuses an already-loaded dirty target scene even with `--force`; save or discard first.
- **macOS paths:** Use real paths (`pwd -P`), not symlinks, for hashing and registry lookups.
- **Instance primary identity:** Registry and CLI routing use canonical `projectRoot` first. The 12-character hash is only for socket/pipe names and user-input fallback; if a hash matches multiple instances, require a project path.
- **No-`--project` routing:** With no `--project`, routing resolves CWD → pinned `activeProjectRoot` (set via `instances use`) → the single live instance. If two or more live instances remain and none is pinned, the command fails with a `CLI_USAGE` error listing candidates instead of silently picking one. Auto-promoted `activeProjectRoot` (most-recent live Editor) is not trusted for this fallback; only an explicit `instances use` pin is.
- **Auth token storage:** Live IPC auth tokens are stored in per-instance 0600 sidecars (`<registryDir>/tokens/<hash>.token`), not in the shared registry. The CLI reads the sidecar during resolve/load, and `InstanceRecord.token` is a non-serialized in-memory field.
- **AI Agent Skill install scope:** Default to project-scoped installs under `<UnityProjectRoot>/.claude/skills/` or `<UnityProjectRoot>/.codex/skills/`; global installs remain available, can shadow project copies, and can be updated or removed from the manager.
- **Scene paths:** Format `/Root[0]/Child[0]` with array notation for sibling indexing; `/` is the virtual scene root.
- **Scene/prefab node flags:** Convenience commands that point at a hierarchy node use `--node`; JSON patch specs still use `target`/`parent`.
- **Prefab editing:** Based on `SerializedProperty.propertyPath` (run `prefab inspect --with-values` to verify paths before patching).
- **Screenshot lightweight options:** `screenshot` defaults to PNG for compatibility; use `--format jpg|jpeg`, `--quality 1-100`, and `--max-width <int>` to reduce agent-facing image size without changing explicit `--width`/`--height` behavior.
- **Record:** Play Mode 전용 Unity Recorder 기반 mp4. `record start`는 `STARTED+recordingId`를 즉시 반환하고, `--duration` 또는 600초 안전캡으로 자동 stop한다. `--wait`는 CLI가 `record status` sidecar를 폴링한다. force-rule 없음. 글로벌 busy에 참여하지 않고 자체 single-flight만 사용한다.
- **Console/test/list trims:** `read-console --no-stacktrace`, `test list --no-detail`, `test run/results --failures-only`, and `instances list --brief` are opt-in response trims only. Defaults preserve full output. Test failure trimming never changes summary counts or cached result files.
- **Test runner:** EditMode는 동기, PlayMode는 비동기(STARTED+runId 즉시 반환). `--wait`로 CLI 측 폴링. `--no-domain-reload`는 PlayMode 전용 속도 옵션이며 정합성 결정과 분리. 동시 실행 1회(`TEST_RUN_IN_PROGRESS` 거부). non-`Completed` 결과는 error envelope/exit code 1로 반환한다. force-rule 없음. 결과는 `Library/com.yhc509.unity-cli-bridge/test-runs/<runId>.json`.
- **QA ui-dump:** `qa ui-dump` dumps clickable UI elements with image-coordinate rect/center fields for direct reuse in `qa click --target` or `qa tap`; `--limit`, `--interactable-only`, `--text`, and `--omit-rect` reduce response size without changing default output. Returned paths are reliable when unique but same-named siblings can be ambiguous, so use center coordinates with `qa tap` in that case. It requires Play Mode and has no force-rule.
- **QA pointer button:** Direct `qa click`, `qa tap`, and `qa swipe` support `--button left|right`; the default is `left` and QA commands keep force-rule None.
- **QA world-dump:** `qa world-dump` lists non-UI world objects that opt in via `IQaTappable` (code) or the `QaTappable` marker component (inspector), returning `label`, hierarchy `path`, image-space center, `onScreen`, and `hasAction`. `--limit` and `--text` reduce response size; filtered responses omit constant `onScreen`/`hasAction` fields only when they carry no per-element signal. Feed a `path` to `qa tap --target`, which invokes the object's `TryQaTap()` action first and falls back to an Input System pointer tap at the object's anchor (reaching raw Input System polling that `qa tap` coordinates/EventSystem cannot). Off-screen excluded unless `--include-offscreen`. Same-named sibling world objects share a path and resolve to the first match; give tappable objects unique names/labels to target them individually. Play Mode required, no force-rule. Markers live in `Runtime/` (namespace `UnityCliBridge.Bridge`), so adding them needs a Unity reimport for `.meta`.
- **QA wait-until:** Conditions are ANDed and polled on `EditorApplication.update`. `--object-exists` waits for active resolve, `--object-interactable` additionally requires `GetInteractableValue` true (objects without an `interactable` property count as true), and `--object-gone` waits for active resolve failure.
- **QA run-sequence:** `qa run-sequence --spec-json <json|@file>` is deferred on `EditorApplication.update`: each step waits for ANDed built-in (`active`/`gone`/`transform`/`scene`/`log`/`interactable`) or `IQaQueryable` query conditions, then runs `key`/`tap`/`swipe`/`wait`/`screenshot` actions. Operators are `==`, `!=`, `>=`, `<=`, `near`, and `changed`; timeouts return `failedStep.unmet` plus `stateSnapshot`. `--record` captures the sequence interval and returns `recordingPath`. Play Mode required, force-rule None. `IQaQueryable` lives in `Runtime/`, so adding it or new implementations needs Unity reimport for `.meta`.
- **New `.cs` files in `unity-package/`:** Never hand-author `.meta` files. Unity assigns the GUID — let it. After adding a new `.cs` file, trigger an Editor reimport (`unity-cli refresh` against the sample project, or focus the Editor) so Unity writes the matching `.meta`. CI's `scripts/check-unity-meta.sh` will fail if a `.cs` ships without its `.meta`. If `unity-cli refresh` is blocked by `PROTOCOL_MISMATCH` because the wire version was just bumped, ask the user to focus the Editor (or do a Reimport All in the package folder) instead — do not fall back to a hand-written GUID.
- **Doc sync:** CLI command or option changes must update all docs. Run through this checklist:
  1. `dotnet run --project cli/UnityCli.DocGen -- --write` — auto-updates `docs/cli-reference.md`
  2. `README.md` — update examples for new/changed commands in both Scene and Prefab sections
  3. `CLAUDE.md` — update Architecture tree if new files are added, update Key Conventions if behavior changes
  4. `tools/skills/unity-cli-operator/SKILL.md` — update command workflows and examples for AI agent usage
  5. `dotnet run --project cli/UnityCli.DocGen -- --check` — verify cli-reference is up to date
- **Release checklist:** Cutting a new version:
  1. `CHANGELOG.md` — move `[Unreleased]` entries to new version section with date
  2. Update `package.json` version
  3. Open a release PR (`chore: release vX.Y.Z`), wait for CI green, then merge
  4. Push an annotated tag (`git tag -a vX.Y.Z -m "Release vX.Y.Z" <commit> && git push origin vX.Y.Z`). This triggers `.github/workflows/release.yml`, which builds artifacts and creates a **draft** GitHub Release.
  5. **Write the GitHub Release body before publishing.** Never publish a release with an empty body.
     - The audience is end users of the package, not contributors. Lead with user-facing impact, not the raw technical change. Translate technical work into the user benefit (e.g. "Implemented Redis caching" → "Dashboards now load up to 3× faster").
     - **No internal codenames or work-classification labels** (e.g. "Bundle X PR0", "F-2", "the recent serializer review"). They are meaningless to readers outside this repo. Describe each change in plain terms.
     - Reuse the prior release's section structure (`Added` / `Changed` / `Fixed` / `Compatibility` / `Notes`). Keep `Compatibility` for wire/protocol/install changes the reader must act on; omit if there are none.
     - Apply with `gh release edit vX.Y.Z --notes-file <path>`.
     - The `release-notes` skill is installed and can help draft this; invoke it when the change set is large.
  6. Publish: `gh release edit vX.Y.Z --draft=false`.

## Branch Policy

- All changes go through PRs to `main`. Direct push to `main` is blocked by branch ruleset.
- Admin bypass exists for emergencies only — do not use it for routine work.
- CI (`test` job) must pass before merge.
- GitHub Codex bot (`@codex`) is enabled as a PR reviewer on this repo.
- Versioning: patch-level increments (`v0.1.0` → `v0.1.1`). Major/minor bumps only when explicitly requested.

## Verification After Changes

- CLI code changes → `dotnet build UnityCliBridge.sln -c Debug`
- Test changes → `dotnet test UnityCliBridge.sln`
- Unity integration changes → test live IPC flows with an actual Unity project
- **Before opening a PR**: after `dotnet test` passes, exercise the change in the Unity Editor against a sample project that imports this package via a `file:` UPM reference. Run the live IPC commands related to the change (no need for a fixed smoke set — scope it to what was touched). If the Editor is not running, launch it. If a script change is not yet picked up, trigger a recompile (`unity-cli refresh` or focus the Editor). Skipping this step is only acceptable for changes that cannot reach Unity (e.g. CLI-only doc/help text).
