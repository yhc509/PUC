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
- 우선순위는 `UNITY_CLI_BIN` 환경 변수, `ucli` (PATH 상의 symlink), 현재 repo의 `dist/unity-cli/unity-cli` 순서다.
- `ucli`가 PATH에 있으면 그대로 사용한다. 매 호출마다 변수를 재정의하지 않는다.
- 셋 다 없으면 repo 루트에서 `./scripts/publish-osx-arm64.sh`로 `dist/unity-cli/unity-cli`를 빌드한 뒤 `ln -s <repo>/dist/unity-cli/unity-cli ~/bin/ucli`로 symlink을 만든다.

2. **대상 프로젝트를 결정한다.**
- CLI는 `--project` 없이도 현재 작업 디렉터리의 Unity 프로젝트, `instances use`로 고정한 핀, 또는 단일 live 인스턴스로 라우팅할 수 있다.
- 다중 live 인스턴스에서 핀이 없으면 후보 목록과 함께 `CLI_USAGE`로 실패한다. 그래도 자동화 작업에서는 의도를 기록하기 위해 가능하면 `--project`를 명시한다.
- 프로젝트 결정 우선순위:
  1. 사용자가 프로젝트를 명시적으로 지정한 경우 → 그대로 사용
  2. 현재 작업 디렉터리(`pwd -P`)가 Unity 프로젝트 내부인 경우 → 해당 프로젝트
  3. 현재 작업 디렉터리가 unity-cli 레포인 경우 → 샘플 프로젝트 `unity-cli-bridge-sample` (개발/테스트 용도)
  4. 여러 프로젝트가 실행 중이면 `instances list --brief`로 확인 후 사용자에게 물어본다
- macOS에서는 항상 `pwd -P`로 실제 경로를 사용한다.
- `--project`는 프로젝트 이름(예: `unity-cli-bridge-sample`)이나 전체 경로 모두 가능하다.
- 다중 인스턴스 환경에서는 `instances list --brief`로 `projectRoot`를 확인하고 `--project <path>` 또는 `--project <projectName>`으로 라우팅한다. 반복 작업에서 생략하고 싶다면 `instances use <projectPath|projectName>`로 기본 대상을 고정한다. 12자 hash 입력은 충돌 시 ambiguous 에러로 거부된다.

3. 쓰기 작업 전에는 상태를 본다.
- 먼저 `status --json --project <name>`으로 live 연결, busy 상태, 현재 프로젝트가 맞는지 확인한다.
- 응답의 `projectName`이 의도한 프로젝트가 맞는지 반드시 확인한다.

4. 작업 종류에 맞는 흐름을 고른다.
- 일반 명령, 에셋 작업, scene inspect/patch는 `references/command-flows.md`
- 프리팹 조립과 patch spec은 `references/prefab-workflows.md`
- 문제 해결은 `references/troubleshooting.md`

5. 작업 뒤에는 반드시 검증한다.
- live 작업 뒤에는 `read-console --limit N --no-stacktrace --output compact` 한 번으로 error/warning/log를 함께 본다. `--type`을 생략하면 모든 타입이 반환되므로 호출을 error/warning 2회로 나눌 필요가 없다. 특정 타입만 필요할 때만 `--type error`로 좁힌다.

## Operating Rules

- 모든 asset 경로는 `Assets/...` 형식으로 다룬다. 조회 전용(`asset find`, `asset info`)은 `Packages/...`도 허용된다.
- 파괴 연산과 덮어쓰기는 `--force`가 있을 때만 허용된다고 가정한다. 다음은 항상 force-gated: `asset delete`, `execute`, `package remove`, `scene remove-component`, `prefab remove-component`. 다음은 조건부 force-gated: 기존 경로를 덮어쓰는 `asset move`/`asset rename`/`asset create`; `scene patch` 안의 `delete-gameobject`/`remove-component`; `prefab patch` 안의 `remove-node`/`remove-component`.
- scene/prefab patch와 asset overwrite는 `Library/com.yhc509.unity-cli-bridge/backups/` 디렉터리에 본체와 `.meta`를 백업한 뒤 실행된다. `BACKUP_RESTORE_FAILED`가 나오면 즉시 중단하고 응답의 백업 경로로 수동 복구를 안내한다.
- `scene patch`는 대상 scene이 이미 dirty이면 `--force`와 무관하게 거부된다. 먼저 저장하거나 변경을 폐기한 뒤 다시 실행한다.
- `execute`/`execute-code`는 임의 C# 실행이므로 항상 `--force`가 필요하다.
- `execute --code 'Debug.Log(__pucArgsJson);' --args '{"k":"v"}' --force`로 넘긴 JSON은 사용자 코드에서 `__pucArgsJson` 문자열 변수로 읽는다.
- 값을 회수해야 하면 사용자 코드에서 `__pucResult = <값>;`에 담는다. 응답의 `hasResult`가 `true`가 되고 `result`에는 타입 보존 JSON이 들어온다(float는 G9, double은 G17 round-trip 포맷).
- Editor 어셈블리의 `[PucCommand]` custom 명령은 `ExecuteValueSerializer.Serialize(obj)`를 반환할 수 있지만, runtime 어셈블리 명령은 정밀 JSON을 직접 직렬화해야 한다.
- 오래 돌 수 있는 `execute`에는 `--timeout <초>`를 붙인다. 기본 30초, 상한 600초이며 협력적 cancel이라 사용자 코드가 `__pucToken`을 체크해야 멈춘다.
- 반복문/대기 코드 생성 시 다음 패턴을 우선 사용한다:

```csharp
for (int i = 0; i < workItems.Count; i++)
{
    __pucToken.ThrowIfCancellationRequested();
    // work
}
```

- `execute --args` 사용자 코드에서는 wrapper 예약 prefix인 `__puc_internal_*` 변수와 `__pucToken`, `__pucResult` 변수를 선언하지 않는다.
- `execute --args` 값에는 secret/credential을 넣지 않는다. CodeDOM 컴파일 중 OS temp에 `.cs` 파일이 잠시 생성될 수 있다.
- **LLM이 소비하는 명령에는 `--output compact`를 기본으로 붙인다.** envelope 메타를 제거하여 토큰을 절약한다.
- `scene patch` 전에는 가능하면 `scene inspect --with-values`를 먼저 실행해서 GameObject path와 field 이름을 확인한다.
- `prefab patch` 전에는 가능하면 `prefab inspect --with-values`를 먼저 실행해서 path와 field 이름을 확인한다. `remove-node`나 `remove-component` 같은 destructive op가 있으면 `--force`를 붙인다.
- inspect 응답이 클 때는 `--max-depth N`으로 깊이를 제한하고 `--omit-defaults`로 기본값을 생략한다. **`--with-values`에는 항상 `--omit-defaults`를 함께 붙여** 0/null/false/빈 값 기본값을 잘라낸다(컴포넌트당 30~55% 절약).
- `material info`도 `--omit-defaults`를 지원한다. URP/Lit 기준 48개 속성 → 변경된 것만 반환하여 토큰을 71% 절약한다.
- `--omit-defaults` 결과는 read-only이다. patch input으로 그대로 쓰면 생략된 필드가 복원되지 않는다.
- `SerializedProperty.propertyPath`는 추측하지 말고 inspect 결과를 기준으로 쓴다.
- live 편집 명령이 compile/update 중이면 읽기 전용 명령만 남기고 나머지는 재시도 흐름으로 본다.
- `package list/add/remove/search`는 bridge에서 비차단으로 처리된다. `PACKAGE_TIMEOUT`이 나오면 Package Manager가 300초 안에 응답하지 않은 상태이므로 Editor의 Package Manager 상태를 확인한 뒤 재시도한다.
- scene path는 `/Root[0]/Child[0]` 형식으로 쓰고 `/`는 virtual scene root로 본다.
- root prefab 이름은 Unity 저장 규칙 때문에 파일 이름으로 정규화된다고 가정한다.
- `screenshot`은 `--view` 생략 시 game이 기본이다. Scene View가 필요하면 `--view scene`을 명시한다.
- **에이전트가 읽을 스크린샷은 `--format jpg --quality 75 --max-width 1024`를 기본으로 붙인다.** 기본 PNG full-resolution은 이미지 토큰을 크게 소비한다(1080p 기준 ~72% 절약). lossless가 필요할 때만 `--format png`.
- Play Mode 영상을 남겨야 하면 `record start --duration N --wait --path /tmp/out.mp4`를 쓴다. 수동 녹화는 `record start` 후 `record status`, `record stop` 순서로 종료한다. `record start`는 Play Mode 전용이며 Unity Recorder dependency가 필요하다.
- `qa tap --x --y`에는 `screenshot`에서 확인한 이미지 좌표를 그대로 넣는다. 응답의 `imageOrigin`은 `top-left`, `coordinateOrigin`은 `bottom-left`다.
- `qa click`, `qa tap`, `qa swipe`는 기본 좌클릭/좌드래그이며, 우클릭 입력 경로를 검증할 때는 `--button right`를 붙인다.
- 별도 Y-flip이나 해상도 스케일 변환은 하지 않는다. Bridge가 마지막 `screenshot` 크기 또는 명시한 `--screenshot-width`/`--screenshot-height`를 기준으로 내부 처리한다.
- 좌표를 추측하지 말고 탭 대상을 먼저 열거한다: uGUI 버튼은 `qa ui-dump --limit 30 --interactable-only --omit-rect --output compact`, 비-UI 월드 오브젝트(전투 그리드 유닛 등)는 `qa world-dump --limit 30 --output compact`. 찾을 텍스트/라벨을 알면 `--text <substring>`으로 서버사이드 필터링한다. 둘 다 `centerX`/`centerY` 이미지 좌표를 그대로 반환하며, 대형 화면 dump에서는 envelope 제거 효과가 특히 크다.
- `qa world-dump`는 게임이 opt-in한 오브젝트만 본다: 게임 컴포넌트가 `UnityCliBridge.Bridge.IQaTappable`을 구현하거나 `QaTappable` 마커를 부착해야 한다. 화면 밖은 `--include-offscreen`을 줄 때만 포함된다.
- 열거된 월드 오브젝트는 `qa tap --target <path>`로 탭한다. 오브젝트의 `TryQaTap()`이 처리하면 그대로, 아니면 anchor 좌표에 Input System 탭을 주입한다 — `qa tap --x --y`(EventSystem)가 닿지 않는 raw Input System polling 입력에도 도달한다. 같은 이름 형제는 첫 매치로만 잡히니 고유 이름/label을 부여한다. 상세는 [references/qa-testing.md](references/qa-testing.md).
- 조건 대기와 다단계 입력은 `qa run-sequence --spec-json @file`로 한 번에 보낸다. 영상이 필요하면 `--record --record-path /tmp/seq.mp4`를 붙여 sequence 구간만 mp4로 남긴다. 상세는 [references/qa-testing.md](references/qa-testing.md).

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

### Test Runner Workflow

코드 수정 뒤에는 가능한 한 관련 테스트만 먼저 좁혀서 돌린다.

1. `.cs`를 수정했다면 `unity-cli refresh --project <project>`로 AssetDatabase 갱신을 요청한다.
2. 관련 EditMode 테스트를 동기 실행한다. `--filter`는 test full name의 대소문자 무시 substring이다: `unity-cli test run --project <project> --mode edit --filter <related-name> --failures-only --output compact`
3. 실패하면 JSON의 failing test name, message, stack trace를 기준으로 원인을 고치고 같은 filter로 다시 실행한다.
4. 통과 뒤에는 필요 범위를 넓힌다: category, assembly, 또는 filter 없는 `unity-cli test run --project <project> --mode edit --failures-only --output compact`.
5. 실행 뒤에는 평소와 같이 `read-console --limit 20 --no-stacktrace --output compact`로 Editor 로그도 확인한다.

PlayMode 자동화는 비동기 시작이 기본이다. 에이전트가 한 번에 결과까지 기다려야 하면 `--wait`를 붙인다.

```bash
unity-cli test run --project <project> --mode play --filter <related-name> --wait --failures-only --output compact
unity-cli test run --project <project> --mode play --filter <related-name> --wait --no-domain-reload --failures-only --output compact
```

`--no-domain-reload`는 PlayMode 전용 속도 옵션이다. static state leakage 위험이 있으므로 테스트가 자체 reset을 보장하거나, 같은 조건에서 재실행해도 결과가 흔들리지 않는 경우에만 사용한다. 이미 시작된 PlayMode run의 결과를 다시 확인해야 하면 응답의 `runId`로 `unity-cli test results --project <project> --run-id <runId> --failures-only --output compact`를 호출한다. non-`Completed` 결과는 error envelope와 CLI exit code 1로 반환된다.

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
