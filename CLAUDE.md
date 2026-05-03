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

```
UnityCliBridge.sln          Solution root for CLI, protocol, DocGen, and tests

cli/UnityCli.Cli/           CLI executable (.NET 9, osx-arm64 + win-x64)
  ├── CliApp.cs              Entry point; handles local status/instances/doctor flows and routes Unity work to IPC
  ├── Services/
  │   ├── CliArgumentParser  Switch-based parser → ParsedCommand
  │   ├── CliCommandCatalog  CLI-side command metadata
  │   ├── LocalIpcClient     Live IPC to running Editor
  │   ├── UnityProjectLocator Project-root resolution and lookup
  │   └── InstanceRegistryStore  Per-project instance tracking
  └── Models/ParsedCommand   CommandKind variants + envelope builder

cli/UnityCli.Protocol/       Shared protocol project compiling linked files from unity-package/com.yhc509.unity-cli-bridge/Runtime/Protocol/

unity-package/com.yhc509.unity-cli-bridge/
  ├── Editor/
  │   ├── BridgeHost.cs       Bridge bootstrap, registry registration, IPC listener, handler orchestration
  │   ├── AssetCommandHandler.cs  Asset CRUD operations and asset metadata
  │   ├── BuiltInAssetCreateProviders.cs  Basic built-in asset create providers
  │   ├── BuiltInAssetCreateProviders.Advanced.cs  Complex/dependency-aware asset providers (partial class)
  │   ├── ComponentOperations.cs  Component list/add/remove shared logic and friendly key resolution
  │   ├── SceneCommandHandler.cs  scene open/inspect/patch and component command entry points
  │   ├── SceneCommandHandler.Patching.cs  Scene patch operation application (partial class)
  │   ├── SceneInspector.cs  Scene graph traversal, node-path resolution, inspect payload building
  │   ├── SceneSpecModels.cs  Scene DTO/spec models
  │   ├── InspectorUtility.cs  Core inspector helpers (layer resolution, vector merge, transform and node-state application)
  │   ├── InspectorJsonWriterUtility.cs  JSON writing helpers for inspect payloads
  │   ├── InspectorPathParserUtility.cs  Scene/prefab path parsing and node-name validation
  │   ├── InspectorMutationReaderUtility.cs  JSON mutation/patch data reading and analysis
  │   ├── InspectorDefaultPruningUtility.cs  Default value pruning for inspect output
  │   ├── PrefabCommandHandler.cs  prefab create/inspect/patch and component command entry points
  │   ├── PrefabCommandHandler.Patching.cs  Prefab patch operation application (partial class)
  │   ├── PrefabInspector.cs  Prefab inspection, node-path resolution, inspect payload building
  │   ├── PrefabSpecModels.cs  Prefab DTO/spec models
  │   ├── SerializedValueApplier.cs  Applies values via SerializedProperty.propertyPath with friendly key fallback
  │   ├── SerializedValueApplier.ComplexTypes.cs  AnimationCurve, Gradient, ManagedReference, Hash128 serialization (partial class)
  │   ├── TypeDiscoveryUtility.cs  Shared component/type scanning utility
  │   ├── BridgeJsonSettings.cs  Shared JSON serializer settings
  │   ├── CliInstallerWindow.cs  EditorWindow for one-click CLI install/update
  │   ├── CliInstallerState.cs   CLI version detection, path resolution, EditorPrefs
  │   └── CliDownloader.cs       GitHub Releases download + archive extraction
  └── Runtime/Protocol/       Shared models (C# 11, nullable enabled)
      ├── CliCommandCatalog.cs  Master command descriptor catalog
      ├── CommandModels.cs      Request/response envelopes
      ├── ProtocolConstants.cs  Registry paths, timeouts, command names
      ├── ProtocolHelpers.cs    Command grouping helpers
      ├── ProtocolJson.cs       Shared JSON serialization helpers
      ├── Registry*.cs          Registry persistence/path models
      └── TransportModels.cs    IPC transport payloads

tests/UnityCli.Cli.Tests/    xUnit tests
```

**Protocol sharing:** `cli/UnityCli.Protocol/` compiles the same `.cs` files from `unity-package/com.yhc509.unity-cli-bridge/Runtime/Protocol/` via `<Compile Include>` links in the `.csproj`. Changes to protocol files affect both the CLI and the Unity package.

## Key Conventions

- **Nullable references enabled** throughout (`#nullable enable`, implicit usings).
- **Asset paths** always use `Assets/...` format.
- **Destructive/dangerous ops require `--force`:** `asset delete` (always), `asset move/rename/create` (when overwriting), destructive scene/prefab patches, scene/prefab `remove-component`, `package remove`, and `execute`.
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
