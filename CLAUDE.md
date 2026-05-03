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

**Shared protocol (`cli/UnityCli.Protocol/` ↔ `unity-package/.../Runtime/Protocol/`)** — The `.csproj` uses `<Compile Include>` links to compile the same `.cs` files from the Unity package. **A change to any protocol file is a change to both sides; keep them buildable for both `.NET 9` and Unity's runtime.** Hot spots:
- `CliCommandCatalog.cs` is the single source of truth for command metadata, including `ForceRule` (None / OnOverwrite / OnDestructiveOp / Always) — every force-gating decision must trace back here.
- `FileBackupTransaction.cs` is `.NET`-testable and powers all backup/restore flows.
- `InstanceRegistryFile.cs` owns the atomic registry-lock protocol (atomic `FileMode.CreateNew`, PID + UTC timestamp content, stale-reclaim via open-then-rename-then-delete) used by both `BridgeHost` and the CLI's `InstanceRegistryStore`.

**Bridge runtime (`unity-package/com.yhc509.unity-cli-bridge/Editor/`)** — Hosted in the Unity Editor.
- `BridgeHost.cs` is the bootstrap and dispatcher: it registers the project in the instance registry, starts the IPC listener (Named Pipe on Windows / Unix socket on macOS+Linux), and routes commands to one of the `*CommandHandler` classes (`Asset`, `AssetCreate`, `Scene`, `Prefab`, `Material`, `Package`, `Qa`, `Screenshot`, `ExecuteCode`, `Custom`).
- Scene/prefab patch logic is deliberately split across `SceneCommandHandler.Patching.cs` and `PrefabCommandHandler.Patching.cs` (partial classes) so the entry-point file stays small and the op-application code lives next to its inspector.
- `SerializedValueApplier.cs` (+ `.ComplexTypes.cs` partial) is the most fragile layer — it translates JSON values into `SerializedProperty.propertyPath` mutations with friendly-key fallback. Run `*-inspect --with-values` before patching to verify paths.
- `AssetBackupTransaction.cs` wraps `FileBackupTransaction` with an `AssetDatabase.Refresh` discipline (always in `finally`) and is the rollback core for scene patch / prefab patch / asset overwrite. Backups land in `Library/com.yhc509.unity-cli-bridge/backups/` to stay outside `Assets/` and `AssetDatabase` scanning.
- `PackageCommandHandler.cs` polls Unity Package Manager from `EditorApplication.update` (deferred dispatch) with a single-flight guard and a 300 s `PACKAGE_TIMEOUT` — never re-introduce blocking polls here.
- `CliInstallerWindow.cs` / `CliInstallerState.cs` / `CliDownloader.cs` / `SkillInstaller.cs` form the `Window > Unity CLI Manager` flow that fetches the matching CLI binary from GitHub Releases and installs the AI-agent skill.

Tests live in `tests/UnityCli.Cli.Tests/` (xUnit, `.NET`-testable surface only). Editor-mode tests for in-Editor handlers live in the package's `Tests~/` folder.

## Key Conventions

- **Nullable references enabled** throughout (`#nullable enable`, implicit usings).
- **Asset paths** always use `Assets/...` format.
- **Destructive/dangerous ops require `--force`:** `asset delete` (always), `asset move/rename/create` (when overwriting), destructive scene/prefab patches, scene/prefab `remove-component`, `package remove`, and `execute`.
- **Patch/overwrite rollback:** Scene/prefab patch and asset overwrite flows use `Library/com.yhc509.unity-cli-bridge/backups/` backups for the asset body and `.meta`; restore failures return backup paths for manual recovery.
- **Dirty scene patch refusal:** `scene patch` refuses an already-loaded dirty target scene even with `--force`; save or discard first.
- **macOS paths:** Use real paths (`pwd -P`), not symlinks, for hashing and registry lookups.
- **Scene paths:** Format `/Root[0]/Child[0]` with array notation for sibling indexing; `/` is the virtual scene root.
- **Scene/prefab node flags:** Convenience commands that point at a hierarchy node use `--node`; JSON patch specs still use `target`/`parent`.
- **Prefab editing:** Based on `SerializedProperty.propertyPath` (run `prefab inspect --with-values` to verify paths before patching).
- **Doc sync:** CLI command or option changes must update all docs. Run through this checklist:
  1. `dotnet run --project cli/UnityCli.DocGen -- --write` — auto-updates `docs/cli-reference.md`
  2. `README.md` — update examples for new/changed commands in both Scene and Prefab sections
  3. `CLAUDE.md` — update Architecture tree if new files are added, update Key Conventions if behavior changes
  4. `tools/skills/unity-cli-operator/SKILL.md` — update command workflows and examples for AI agent usage
  5. `dotnet run --project cli/UnityCli.DocGen -- --check` — verify cli-reference is up to date
- **Release checklist:** Before tagging a new version:
  1. `CHANGELOG.md` — move `[Unreleased]` entries to new version section with date
  2. Update `package.json` version

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
