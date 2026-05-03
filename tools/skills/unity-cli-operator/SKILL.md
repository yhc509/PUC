---
name: unity-cli-operator
description: "Unity Editor 외부 제어 1차 진입점. 씬/프리팹/에셋/콘솔/Play Mode QA/메뉴/스크린샷/컴파일 등 Unity 관련 모든 외부 작업. **`Unity -batchmode` 헤드리스 실행이나 Unity MCP 서버를 시도하기 전에 먼저 검토.** 호출은 Bash로 `ucli`. Triggers: Unity 씬/프리팹/에셋/콘솔, Play Mode, QA, batchmode, 헤드리스, MCP, unity-cli, ucli, Unity CLI Bridge."
---

# Unity CLI Operator

`unity-cli`를 실제 작업에 안전하게 쓰기 위한 운용 스킬이다. 목적은 명령어 목록을 길게 나열하는 것이 아니라, 현재 프로젝트를 올바르게 잡고, 맞는 명령을 고르고, 작업 뒤 로그까지 확인하는 흐름을 고정하는 것이다.

## 진입 규칙

- Unity 외부 제어가 필요한 모든 작업은 이 스킬부터 검토.
- `Unity -batchmode` 헤드리스, Unity MCP 서버 탐색은 unity-cli로 못하는 게 확인된 뒤에만.
- 이 레포는 MCP 없음. `batch` 서브커맨드 없음. 다중 작업은 ucli를 N번 호출 (의존성 없으면 병렬, 있으면 순차).

## Quick Workflow

1. 실행 파일을 찾는다.
- 우선순위는 `UNITY_CLI_BIN` 환경 변수, `ucli` (PATH 상의 symlink), 현재 repo의 `dist/unity-cli/UnityCli.Cli` 순서다.
- `ucli`가 PATH에 있으면 그대로 사용한다. 매 호출마다 변수를 재정의하지 않는다.
- 셋 다 없으면 `ln -s ~/dev/unity/unity-cli/dist/unity-cli/UnityCli.Cli ~/bin/ucli`로 symlink을 만든다.

2. **대상 프로젝트를 결정하고 `--project`를 항상 명시한다.**
- CLI는 `--project` 없이 호출하면 아무 live 인스턴스에 연결한다. **의도하지 않은 프로젝트에 명령이 실행될 수 있으므로 반드시 명시한다.**
- 프로젝트 결정 우선순위:
  1. 사용자가 프로젝트를 명시적으로 지정한 경우 → 그대로 사용
  2. 현재 작업 디렉터리(`pwd -P`)가 Unity 프로젝트 내부인 경우 → 해당 프로젝트
  3. 현재 작업 디렉터리가 unity-cli 레포인 경우 → 샘플 프로젝트 `unity-cli-bridge-sample` (개발/테스트 용도)
  4. 여러 프로젝트가 실행 중이면 `instances list`로 확인 후 사용자에게 물어본다
- macOS에서는 항상 `pwd -P`로 실제 경로를 사용한다.
- `--project`는 프로젝트 이름(예: `unity-cli-bridge-sample`)이나 전체 경로 모두 가능하다.

3. 쓰기 작업 전에는 상태를 본다.
- 먼저 `status --json --project <name>`으로 live 연결, busy 상태, 현재 프로젝트가 맞는지 확인한다.
- 응답의 `projectName`이 의도한 프로젝트가 맞는지 반드시 확인한다.

4. 작업 종류에 맞는 흐름을 고른다.
- 일반 명령, 에셋 작업, scene inspect/patch는 `references/command-flows.md`
- 프리팹 조립과 patch spec은 `references/prefab-workflows.md`
- 문제 해결은 `references/troubleshooting.md`

5. 작업 뒤에는 반드시 검증한다.
- live 작업 뒤에는 `read-console --type error --limit N`
- live 작업 뒤에는 `read-console --type warning --limit N`

## Operating Rules

- 모든 asset 경로는 `Assets/...` 형식으로 다룬다. 조회 전용(`asset find`, `asset info`)은 `Packages/...`도 허용된다.
- 파괴 연산과 덮어쓰기는 `--force`가 있을 때만 허용된다고 가정한다. 특히 asset delete/overwrite, scene `delete-gameobject`/`remove-component`, prefab `remove-node`/`remove-component`, `package remove`는 force-gated다.
- scene/prefab patch와 asset overwrite는 같은 폴더의 hidden `.bridge-bak` 파일로 본체와 `.meta`를 백업한 뒤 실행된다. `BACKUP_RESTORE_FAILED`가 나오면 즉시 중단하고 응답의 백업 경로로 수동 복구를 안내한다.
- `scene patch`는 대상 scene이 이미 dirty이면 `--force`와 무관하게 거부된다. 먼저 저장하거나 변경을 폐기한 뒤 다시 실행한다.
- `execute`/`execute-code`는 임의 C# 실행이므로 항상 `--force`가 필요하다.
- `execute --code 'Debug.Log(__pucArgsJson);' --args '{"k":"v"}' --force`로 넘긴 JSON은 사용자 코드에서 `__pucArgsJson` 문자열 변수로 읽는다.
- `execute --args` 사용자 코드에서는 wrapper 예약 prefix인 `__puc_internal_*` 변수를 선언하지 않는다.
- `execute --args` 값에는 secret/credential을 넣지 않는다. CodeDOM 컴파일 중 OS temp에 `.cs` 파일이 잠시 생성될 수 있다.
- **LLM이 소비하는 명령에는 `--output compact`를 기본으로 붙인다.** envelope 메타를 제거하여 토큰을 절약한다.
- `scene patch` 전에는 가능하면 `scene inspect --with-values`를 먼저 실행해서 GameObject path와 field 이름을 확인한다.
- `prefab patch` 전에는 가능하면 `prefab inspect --with-values`를 먼저 실행해서 path와 field 이름을 확인한다. `remove-node`나 `remove-component` 같은 destructive op가 있으면 `--force`를 붙인다.
- inspect 응답이 클 때는 `--max-depth N`으로 깊이를 제한하고 `--omit-defaults`로 기본값을 생략한다.
- `material info`도 `--omit-defaults`를 지원한다. URP/Lit 기준 48개 속성 → 변경된 것만 반환하여 토큰을 71% 절약한다.
- `--omit-defaults` 결과는 read-only이다. patch input으로 그대로 쓰면 생략된 필드가 복원되지 않는다.
- `SerializedProperty.propertyPath`는 추측하지 말고 inspect 결과를 기준으로 쓴다.
- live 편집 명령이 compile/update 중이면 읽기 전용 명령만 남기고 나머지는 재시도 흐름으로 본다.
- scene path는 `/Root[0]/Child[0]` 형식으로 쓰고 `/`는 virtual scene root로 본다.
- root prefab 이름은 Unity 저장 규칙 때문에 파일 이름으로 정규화된다고 가정한다.
- `screenshot`은 `--view` 생략 시 game이 기본이다. Scene View가 필요하면 `--view scene`을 명시한다.
- `qa tap --x --y`에는 `screenshot`에서 확인한 이미지 좌표를 그대로 넣는다. 응답의 `imageOrigin`은 `top-left`, `coordinateOrigin`은 `bottom-left`다.
- 별도 Y-flip이나 해상도 스케일 변환은 하지 않는다. Bridge가 마지막 `screenshot` 크기 또는 명시한 `--screenshot-width`/`--screenshot-height`를 기준으로 내부 처리한다.

### Script Workflow (No Dedicated Commands)

unity-cli does not have dedicated script create/delete commands. Use this combination:

**Create/modify script:**
1. Write .cs file directly to the Assets/ folder via filesystem
2. `unity-cli refresh` — trigger AssetDatabase refresh
3. `unity-cli compile` — request script compilation
4. `unity-cli read-console --type error` — check for compile errors

**Delete script:**
- `unity-cli asset delete --path Assets/Scripts/MyScript.cs --force`
  (handles .meta cleanup and refresh automatically)

### Component Operations

**List components on a node:**
- `unity-cli scene list-components --node "/Player[0]"`
- `unity-cli prefab list-components --path Assets/Prefabs/Player.prefab --node "/Root[0]"`

**Add component (with optional initial values):**
- `unity-cli scene add-component --path Assets/Scenes/S.unity --node "/Player[0]" --type Rigidbody --values '{"mass":5,"damping":1}'`
- `unity-cli prefab add-component --path Assets/Prefabs/P.prefab --node "/Root[0]" --type BoxCollider`

**Remove component:**
- `unity-cli scene remove-component --path Assets/Scenes/S.unity --node "/Player[0]" --type BoxCollider --force`
- `unity-cli prefab remove-component --path Assets/Prefabs/P.prefab --node "/Root[0]" --type BoxCollider --force`

**Friendly key mapping:** Rigidbody, Collider, Renderer, Light, and Camera values accept common keys like `mass`, `damping`, `isTrigger`, `materials[0]`, `shadowStrength`, and `fieldOfView`, which resolve to Unity `SerializedProperty.propertyPath` values. If a key is not found, use `list-components` then `inspect --with-values`.

## Convenience Commands — 편의 명령 우선 사용 원칙

아래 작업에는 `scene patch --spec-json` 대신 전용 편의 명령을 우선 사용한다. 호출 횟수와 토큰을 절약할 수 있다.

| 작업 | 편의 명령 | 대체했던 방식 |
|------|-----------|-------------|
| 프리미티브 GO 추가 | `scene add-object --primitive Cube --parent ... --position x,y,z` | add-object (빈 GO) + inspect + patch Transform 3단계 |
| Transform 수정 | `scene set-transform --node ... --position/--rotation/--scale` | scene patch modify-component |
| 머티리얼 할당 | `scene assign-material --node ... --material Assets/...` | inspect + scene patch m_Materials.Array.data[0] |
| 타입 기반 에셋 검색 | `asset find --type Material` | --name 필수였음 |

- `scene add-object` 응답에는 `createdPath`가 포함되므로, 후속 명령에서 별도 inspect 없이 바로 경로를 사용할 수 있다.
- scene/prefab hierarchy node를 가리키는 편의 명령은 `--node`를 사용한다. JSON patch spec 내부 필드는 계속 `target`/`parent`를 사용한다.
- `set-node`에서 인식 안 되는 키를 넣으면 `warnings` 필드로 경고가 반환된다. 모든 키가 실패하면 `patched: false`가 된다.

## What To Read Next

- 일반 운용, 에셋 생성, scene inspect/patch: [references/command-flows.md](references/command-flows.md)
- prefab create/inspect/patch와 spec 작성: [references/prefab-workflows.md](references/prefab-workflows.md)
- QA 테스트 자동화 (Play Mode 입력 시뮬레이션): [references/qa-testing.md](references/qa-testing.md)
- stale instance, busy, liveReachable, 로그 확인: [references/troubleshooting.md](references/troubleshooting.md)
- 빠르게 시작할 JSON 템플릿: `assets/`
