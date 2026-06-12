# unity-cli-bridge

Control the Unity Editor from the command line. No manual server startup, no port management — the bridge starts when the Editor opens, and the CLI finds the right project automatically.

## Design Philosophy

Most Unity automation tools fall into two camps:

**MCP mega-tools** wrap dozens of actions behind a Python/Node intermediary. Every call hops through AI → MCP server → HTTP → Unity plugin. More moving parts, more latency, more failure modes.

**Dynamic code execution** sends raw C# to Unity and reads results from Debug.Log. Infinitely flexible, but the LLM must generate correct C# every time, and reading results requires a second call.

**unity-cli-bridge takes a third path: declarative commands over direct IPC.**

```
CLI ──── local IPC ──── Unity Editor (bridge)
         (Named Pipe on Windows, Unix socket on macOS/Linux)
```

- **One hop, no intermediary.** CLI talks directly to the Editor over local IPC. Average response: 265 ms.
- **Declarative, not imperative.** `scene add-object --primitive Cube --position 3,0,0` instead of writing C# code. The LLM picks options, not APIs.
- **Token-aware responses.** `--output compact` strips envelope metadata. `--omit-defaults` cuts material info by 71% and scene inspect by 41%.
- **Structured results in stdout.** No polling, no log scraping, no file reads. Every command returns JSON immediately.
- **Fail fast, fail loud.** Invalid options get a clear error. Unrecognized patch keys return warnings instead of silent success.

> See [Benchmark: 3-Way Comparison](https://github.com/yhc509/unity-cli-bridge/wiki/Benchmark-Unity-Editor-CLI-Tool-Comparison) for measured results against two other approaches on the same scenario.

## Quick Start

### 1. Add the Unity Package

**Option A: Unity Package Manager**

In Unity, open Package Manager and choose **Add package from git URL**. Paste:

```
https://github.com/yhc509/unity-cli-bridge.git?path=/unity-package/com.yhc509.unity-cli-bridge#main
```

**Option B: Edit `Packages/manifest.json` manually**

Add the following to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.yhc509.unity-cli-bridge": "https://github.com/yhc509/unity-cli-bridge.git?path=/unity-package/com.yhc509.unity-cli-bridge#main"
  }
}
```

The bridge starts automatically when the Editor opens. No configuration needed.

### 2. Install the CLI

**Option A: From Unity Editor** (recommended)

Open `Window > Unity CLI Manager` in the Editor menu. Click **Install CLI** — the correct binary is downloaded automatically to `~/.unity-cli-bridge/unity-cli/`.

**Option B: Manual download**

Download from [GitHub Releases](https://github.com/yhc509/unity-cli-bridge/releases):

| Platform | File |
|----------|------|
| macOS (Apple Silicon) | `unity-cli-osx-arm64.tar.gz` |
| Windows (x64) | `unity-cli-win-x64.zip` |

Extract and add the binary to your PATH.

> **Tip:** A short, fixed install path (`~/.unity-cli-bridge/unity-cli/`) saves tokens when AI agents invoke the CLI repeatedly — every character in the path is repeated on each call.

### 3. Install AI Agent Skill

In the Unity Editor, open `Window > Unity CLI Manager`. Select your AI tool (Claude Code or Codex) from the dropdown and click **Install Skill**.

| Tool | Install Path |
|------|-------------|
| Claude Code | `~/.claude/skills/unity-cli-bridge/` |
| Codex | `~/.codex/skills/unity-cli-bridge/` |

The skill teaches AI agents how to pick the right commands, run them safely, and verify results with `read-console`.

### 4. Verify

Check that the CLI can reach the running Editor:

```bash
unity-cli status --project /path/to/your-project --json
```

> If two worktrees happen to collide on the same 12-character SHA256 prefix, the bridge still listens on separate socket/pipe names. Use `--project <path>` to route by canonical project root.

When `--project` is omitted, the CLI first uses the current Unity project directory, then an explicit default set with `unity-cli instances use <projectPath|projectName>`, then the single live Editor if exactly one is running. If multiple live Editors remain and none is pinned, the command fails with a usage error that lists candidates instead of guessing.

## What You Can Do

### Editor Control

```bash
unity-cli status --project MyGame          # Editor state, Unity version, current scene
unity-cli play / pause / stop              # Play Mode control
unity-cli compile                          # Trigger recompile
unity-cli compile --wait                   # Wait until compile/import finishes and bridge is reachable
unity-cli refresh --wait                   # Refresh assets and wait for Editor readiness
unity-cli screenshot --path /tmp/shot.png  # Game View capture (default), or --view scene
unity-cli screenshot --format jpg --quality 70 --max-width 1024 --path /tmp/shot.jpg
unity-cli read-console --type error        # Check for errors after any operation
unity-cli execute-menu --list "GameObject" # Browse Unity menus
unity-cli execute --code "Debug.Log(1);" --force  # Run arbitrary C# (escape hatch)
unity-cli execute --code "Debug.Log(__pucArgsJson);" --args '{"k":"v"}' --force
unity-cli execute --code "__pucResult = new Vector3(1.5f, 0f, 3.25f);" --force
# 협력적 timeout — 사용자 코드는 __pucToken 체크 필요
unity-cli execute --file my-script.cs --force --timeout 60
```

`execute --args` exposes the supplied JSON as `__pucArgsJson`; do not declare variables with the reserved `__puc_internal_*` prefix in user code. Avoid putting secrets or credentials in `--args`, because CodeDOM compilation can briefly create temporary `.cs` files under the OS temp directory.

Assign a value to `__pucResult` when the caller needs a structured return value. The response includes `hasResult: true` and `result` as type-preserving JSON, with `float` formatted using G9 and `double` using G17 for round-trip precision. Editor-assembly custom `[PucCommand]` methods can return `ExecuteValueSerializer.Serialize(obj)`; runtime-assembly commands must serialize precise JSON themselves.

`--timeout`(기본 30초, 상한 600초)은 **협력적** cancel입니다. 사용자 코드가 `__pucToken.ThrowIfCancellationRequested()`(또는 다른 token API)를 호출해야 강제 종료됩니다. 호출하지 않으면 main thread를 계속 점유하므로 `--force`를 쓰는 사용자가 책임집니다.

### Assets

```bash
unity-cli asset find --type Material                    # Find by type
unity-cli asset find --name Player --type Prefab        # Find by name + type
unity-cli asset info --path Assets/Scenes/Main.unity    # Asset metadata
unity-cli asset create --type material --path Assets/Materials/Red  # Create
unity-cli asset mkdir --path Assets/NewFolder            # Create folder
unity-cli asset move --from ... --to ... --force         # Move/rename
unity-cli asset delete --path ... --force                # Delete
```

### Scenes

```bash
# Open and inspect
unity-cli scene open --path Assets/Scenes/Main.unity
unity-cli scene inspect --path ... --with-values --omit-defaults

# Build scenes with convenience commands
unity-cli scene add-object --name Cube --primitive Cube \
  --parent "/Environment[0]" --position 3,0,0
# → Response includes createdPath — no follow-up inspect needed

unity-cli scene set-transform --node "/Cube[0]" --position 0,1,0 --scale 2,2,2
unity-cli scene assign-material --node "/Cube[0]" --material Assets/Materials/Red.mat

# Component operations
unity-cli scene list-components --node "/Cube[0]"
unity-cli scene add-component --path Assets/Scenes/Main.unity --node "/Player[0]" --type Rigidbody --values '{"mass":5}'
unity-cli scene remove-component --path Assets/Scenes/Main.unity --node "/Player[0]" --type BoxCollider --index 0 --force

# Or use spec-based patching for complex edits
unity-cli scene patch --path ... --spec-file patch.json
```

### Prefabs

```bash
unity-cli prefab create --path Assets/Prefabs/Enemy.prefab \
  --spec-json '{"root":{"name":"Enemy","children":[...]}}'
unity-cli prefab inspect --path ... --with-values
unity-cli prefab patch --path ... --spec-json '{"operations":[...]}'
unity-cli prefab patch --path ... --spec-file destructive-patch.json --force

# Component operations
unity-cli prefab list-components --path Assets/Prefabs/Player.prefab --node "/Root[0]"
unity-cli prefab add-component --path Assets/Prefabs/Player.prefab --node "/Root[0]" --type Rigidbody --values '{"mass":5}'
unity-cli prefab remove-component --path Assets/Prefabs/Player.prefab --node "/Root[0]" --type BoxCollider --force
```

Patch ops: `add-child`, `remove-node`, `set-node`, `add-component`, `remove-component`, `set-component-values`

Friendly component keys are available in scene/prefab value patches for common Unity components:
- Rigidbody: `mass`, `damping`, `angularDamping`, `useGravity`, `isKinematic`, `constraints`
- Collider family: `isTrigger`, `material`, `contactOffset`, plus `size`, `radius`, `height`, `mesh`, `convex`
- Renderer family: `materials`, `materials[0]`, `receiveShadows`, `shadowCastingMode`, `lightProbeUsage`
- Light: `color`, `intensity`, `range`, `type`, `shadows`, `shadowStrength`
- Camera: `fieldOfView`, `nearClipPlane`, `farClipPlane`, `backgroundColor`, `orthographicSize`

### Materials

```bash
unity-cli material info --path Assets/Materials/Red.mat --omit-defaults
unity-cli material set --path ... --property _BaseColor --value 1,0,0,1
unity-cli material set --path ... --texture _MainTex --asset Assets/Textures/wood.png
```

### Packages

```bash
unity-cli package list
unity-cli package add --name com.unity.inputsystem
unity-cli package remove --name ... --force
unity-cli package search --query "input"
```

Package Manager commands use a 360-second CLI live timeout so the bridge can return its 300-second `PACKAGE_TIMEOUT` response. They are single-flight inside the Editor; a second package command returns `PACKAGE_BUSY` until the active request completes.

### Test Runner

```bash
unity-cli test list --mode all
unity-cli test run --mode edit --filter PlayerControllerTests
unity-cli test run --mode play --filter Smoke --wait
unity-cli test results --run-id <runId>
```

`test run --mode edit` returns a synchronous result payload. `test run --mode play` starts the run and returns `STARTED` plus a `runId`; add `--wait` when the CLI should poll until the PlayMode result is ready. `--filter` is a case-insensitive substring match against each test's full name (`Namespace.Class.Method`), so a value like `Smoke` also matches every method on a `SmokeTests` class — when the class and method share a prefix, narrow it with a more specific fragment such as `Smoke_`. Cached results are stored under `Library/com.yhc509.unity-cli-bridge/test-runs/<runId>.json` and can be retrieved with `test results`. A non-`Completed` cached run result returns an error envelope and CLI exit code 1.

`--no-domain-reload` is an opt-in PlayMode-only speed option. It can avoid 30-120 seconds of domain reload overhead, but static state can leak between runs, so only use it for suites that reset their own state:

```bash
unity-cli test run --mode play --filter Smoke --wait --no-domain-reload
```

A typical AI repair loop is: make a focused code change, `unity-cli refresh`, run the relevant EditMode filter, inspect failing test names and messages from the JSON result, patch the code, and rerun the same command until it passes.

### Play Mode QA

```bash
unity-cli qa click --qa-id StartButton
unity-cli screenshot --view game --path /tmp/qa-reference.png
unity-cli screenshot --view game --format jpg --quality 70 --max-width 1024 --path /tmp/qa-reference.jpg
unity-cli qa ui-dump --json
unity-cli qa world-dump --json
unity-cli qa tap --x 400 --y 300
unity-cli qa tap --target /Battle/Units/Unit_Erich
unity-cli qa tap --target /Battle/EndTurnZone --button right
unity-cli qa swipe --from 100,200 --to 300,400
unity-cli qa swipe --target ... --from 0,0 --to 100,0 --duration 500
unity-cli qa key --key space
unity-cli qa wait-until --scene GameScene --timeout 5000
unity-cli qa wait-until --object-interactable StartButton --timeout 5000
unity-cli qa wait-until --object-gone LoadingSpinner --timeout 5000
unity-cli qa run-sequence --spec-json @seq.json --timeout 60000
```

`qa ui-dump` returns clickable UI elements with hierarchy `path`, visible `text`, `interactable`, and image-space rect/center fields. Feed a returned `path` to `qa click --target`, or `centerX`/`centerY` to `qa tap --x --y`.

`qa world-dump` lists non-UI world objects that opt in via `IQaTappable` (implement on your own component) or the `QaTappable` marker component. Each entry has a `label`, hierarchy `path`, image-space `centerX`/`centerY`, `onScreen`, and `hasAction`. Feed a `path` to `qa tap --target`: the bridge invokes the object's `TryQaTap()` action when present, otherwise simulates an Input System tap at the object's anchor — so it reaches games that poll the Input System directly. Off-screen objects are excluded unless `--include-offscreen` is passed.

`qa click`, `qa tap`, and `qa swipe` default to `--button left`; pass `--button right` to drive right-click or right-drag input paths.

When multiple `qa wait-until` conditions are supplied, every condition must be satisfied (AND). `qa wait-until --object-interactable` waits for an active target whose effective interactable state is true; non-Selectable objects without that state are treated as interactable once active. `qa wait-until --object-gone` waits until an active target can no longer be resolved, which covers deactivated or destroyed objects.

`qa run-sequence` sends a JSON `steps` array to the bridge in one deferred request. Each step waits for all conditions, then runs its actions without a per-step CLI round trip. Conditions can check `active`, `gone`, `transform`, `scene`, `log`, `interactable`, or game state exposed by `IQaQueryable`; `transform` and `query` conditions support `==`, `!=`, `>=`, `<=`, `near`, and `changed`. Actions support `key`, `tap`, `swipe`, and `wait`. On timeout the response reports `completedSteps`, `failedStep.unmet`, and `failedStep.stateSnapshot`.

```json
{
  "steps": [
    {
      "name": "confirm-ready",
      "wait": [
        { "target": "/State", "query": "phase", "op": "==", "value": "Ready" }
      ],
      "actions": [
        { "key": "space" }
      ]
    }
  ]
}
```

Implement `UnityCliBridge.Bridge.IQaQueryable` on a game component to expose state values for `query` conditions. Supported return values are numbers, bool, string, `Vector2`, and `Vector3`; unknown keys should return `false`.

Same-named sibling world objects share a path and resolve to the first match; give tappable objects unique names/labels to target them individually.

`screenshot` responses include both image size (`width`/`height`) and live input metadata (`screenWidth`/`screenHeight`, `imageOrigin=top-left`, `coordinateOrigin=bottom-left`). `qa tap` takes screenshot image coordinates as-is, reuses the last successful `screenshot` dimensions when `--screenshot-width`/`--screenshot-height` are omitted, and lets the bridge handle Y-flip plus resolution scaling into Unity screen space. See [qa-testing.md](tools/skills/unity-cli-operator/references/qa-testing.md) for the coordinate workflow.

## Token Optimization

For AI agent workflows, minimize token consumption:

```bash
# --output compact: strip envelope metadata, return data payload only
unity-cli asset info --path ... --output compact

# --omit-defaults: strip default/identity values from inspect and material responses
unity-cli scene inspect --path ... --with-values --omit-defaults    # 41% smaller
unity-cli material info --path ... --omit-defaults                  # 71% smaller

# --max-depth: limit hierarchy traversal depth
unity-cli scene inspect --path ... --max-depth 2
```

## Repository Structure

```
cli/UnityCli.Cli/              CLI executable (.NET 9, macOS arm64 + Windows x64)
cli/UnityCli.Protocol/         Shared protocol (linked from Unity package)
unity-package/                 com.yhc509.unity-cli-bridge (UPM package)
tools/skills/unity-cli-operator/   AI agent skill (dev copy; end-users install via CLI Manager)
tests/                         xUnit CLI tests + live IPC test scenarios
docs/                          Generated CLI reference, specs
```

## Safety Rules

- **Destructive or dangerous ops require `--force`:** `asset delete`, `execute`, `package remove`, `scene remove-component`, `prefab remove-component` (always); `asset move` / `asset rename` / `asset create` when overwriting an existing path; destructive operations inside `scene patch` (`delete-gameobject`, `remove-component`) and `prefab patch` (`remove-node`, `remove-component`).
- **Raw force payloads are explicit:** `raw --force` only injects `force=true` when the payload omits `force` or already sets it to `true`; conflicting payload values fail fast.
- **Patch and overwrite rollback:** Scene/prefab patch and asset overwrite flows create backups for the asset body and `.meta` under `Library/com.yhc509.unity-cli-bridge/backups/`, keeping rollback files outside `Assets/` and normal git tracking.
- **Dirty scene patch refusal:** `scene patch` refuses a target scene that is already loaded with unsaved changes, even with `--force`; save or discard the scene first.
- **Asset paths:** Write operations are `Assets/...` only. Reads allow `Packages/...` too.
- **Package Manager requests:** `package list`, `package add`, `package remove`, and `package search` use a longer live timeout and return `PACKAGE_BUSY` if another package command is already running.
- **Scene paths:** `/Root[0]/Child[0]` format with sibling indices. `/` is the virtual scene root.
- **Inspect before patch:** Always `scene inspect --with-values` or `prefab inspect --with-values` before writing patch specs.
- **Friendly component keys:** Common Rigidbody, Collider, Renderer, Light, and Camera patch keys are resolved to Unity `SerializedProperty.propertyPath` values.
- **set-node warnings:** Unrecognized keys now return warnings instead of silent success.

## Documentation

- [CLI Reference (generated)](docs/cli-reference.md)
- [Architecture](docs/architecture.md)
- [Scene Spec](docs/scene-spec.md)
- [Prefab Spec](docs/prefab-spec.md)
- [Benchmark](https://github.com/yhc509/unity-cli-bridge/wiki/Benchmark-Unity-Editor-CLI-Tool-Comparison)
- [Unity Package](unity-package/com.yhc509.unity-cli-bridge/README.md)

## AI Agent Skill

The `unity-cli-bridge` skill teaches AI agents how to use the CLI safely: pick the right command, verify with `read-console`, follow inspect-before-patch patterns.

Install from **Window > Unity CLI Manager** in the Unity Editor — select your AI tool and click **Install Skill**. Supports Claude Code and Codex.

## Development

```bash
dotnet build UnityCliBridge.sln -c Debug
dotnet test UnityCliBridge.sln
dotnet run --project cli/UnityCli.DocGen -- --check   # Verify docs match code
```

## Current Limits

- macOS arm64 and Windows x64 supported
- Live IPC required — commands fail fast when no Editor is running
- Scene patching targets saved `Assets/...unity` scenes; multi-scene orchestration is out of scope
- Prefab-internal object references and nested variants are not yet supported
