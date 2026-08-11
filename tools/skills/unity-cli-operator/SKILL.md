---
name: unity-cli-operator
description: "Unity Editor 외부 제어 1차 진입점. 씬/프리팹/에셋/콘솔/Play Mode QA/메뉴/스크린샷/컴파일 등 Unity 관련 모든 외부 작업. **`Unity -batchmode` 헤드리스 실행이나 Unity MCP 서버를 시도하기 전에 먼저 검토.** 호출은 Bash로 `ucli`. Triggers: Unity 씬/프리팹/에셋/콘솔, Play Mode, QA, batchmode, 헤드리스, MCP, unity-cli, ucli, Unity CLI Bridge."
---

# Unity CLI Operator

`unity-cli`를 실제 작업에 안전하게 쓰기 위한 운용 스킬이다. 목적은 명령어 목록을 길게 나열하는 것이 아니라, 현재 프로젝트를 올바르게 잡고, 맞는 명령을 고르고, 작업 뒤 로그까지 확인하는 흐름을 고정하는 것이다.

## 진입 규칙

- Unity 외부 제어가 필요한 모든 작업은 이 스킬부터 검토.
- `Unity -batchmode` 직접 실행, Unity MCP 서버 탐색은 unity-cli로 못하는 게 확인된 뒤에만. 헤드리스 기동 자체는 `editor launch`가 담당한다 (아래 Editor Lifecycle).
- 이 레포는 MCP 없음. `batch` 서브커맨드 없음. 다중 작업은 ucli를 N번 호출 (의존성 없으면 병렬, 있으면 순차).

## Quick Workflow

1. 실행 파일을 찾는다.
- 우선순위, `ucli` alias, 설치 fallback은 [references/command-flows.md](references/command-flows.md)의 `실행 파일 찾기`를 따른다.

2. **대상 프로젝트를 결정한다.**
- `--project` 명시, `pwd -P`, 다중 인스턴스 라우팅, `instances use`는 [references/command-flows.md](references/command-flows.md)의 `프로젝트 결정`을 따른다.

3. 쓰기 작업 전에는 상태를 본다.
- 먼저 `status --json --project <name>`으로 live 연결, busy 상태, 현재 프로젝트가 맞는지 확인한다.
- 응답의 `projectName`이 의도한 프로젝트가 맞는지 반드시 확인한다.
- live 인스턴스가 없으면 사용자에게 요청하지 말고 `editor launch`로 직접 기동한다 (아래 Editor Lifecycle).

4. 작업 종류에 맞는 흐름을 고른다.
- 일반 명령, 에셋 작업, scene inspect/patch는 `references/command-flows.md`
- 프리팹 조립과 patch spec은 `references/prefab-workflows.md`
- 문제 해결은 `references/troubleshooting.md`

5. 작업 뒤에는 반드시 검증한다.
- live 작업 뒤 검증 루틴과 `read-console --no-stacktrace --output compact` 패턴은 [references/command-flows.md](references/command-flows.md)의 `검증 루틴`을 따른다.

## Editor Lifecycle

대상 프로젝트의 에디터가 안 떠 있으면 사용자에게 요청하지 말고 직접 헤드리스로 기동한다:

```bash
unity-cli editor launch --project <path>        # 헤드리스(-batchmode) 기동, 브릿지 준비까지 대기
unity-cli editor stop --project <path>          # graceful 종료 (미저장 변경 있으면 EDITOR_DIRTY; --force로 폐기)
```

- `editor launch`는 멱등이다 — live 인스턴스가 있으면 재사용하고 `"reused": true`를 반환하므로, 워크플로우 시작 전에 호출해도 안전하고 이중 기동 모달 행을 예방한다.
- 기본 헤드리스는 GPU를 유지한다: 창 없이도 screenshot/record/qa가 동작한다. `--nographics`를 줄 때만 렌더링 명령이 `HEADLESS_NO_GRAPHICS`로 거부된다. 창이 필요하면 `--gui`.
- 등록 안 된 에디터 프로세스가 프로젝트를 이미 열고 있으면 `EDITOR_ALREADY_RUNNING_CONFLICT`로 거부된다(재시도 가능 — 부팅/컴파일 중일 수 있다). 기동 로그는 `Library/com.yhc509.unity-cli-bridge/editor-launch.log`.
- 첫 임포트가 긴 프로젝트에서 `EDITOR_WAIT_TIMEOUT`이 나오면 `--timeout <초>`를 늘려 재시도한다(기본 300초).

## Operating Rules

- 모든 asset 경로는 `Assets/...` 형식으로 다룬다. 조회 전용(`asset find`, `asset info`)은 `Packages/...`도 허용된다.
- 파괴 연산과 덮어쓰기는 `--force`가 있을 때만 허용된다고 가정한다. 다음은 항상 force-gated: `asset delete`, `execute`, `package remove`, `scene remove-component`, `prefab remove-component`. 다음은 조건부 force-gated: 기존 경로를 덮어쓰는 `asset move`/`asset rename`/`asset create`/`prefab create`; `scene patch` 안의 `delete-gameobject`/`remove-component`; `prefab patch` 안의 `remove-node`/`remove-component`.
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
- **LLM이 소비하는 명령에는 `--output compact`를 기본으로 붙인다.** 이유와 예외는 [references/command-flows.md](references/command-flows.md)의 `상태 확인` 절 설명을 따른다.
- `scene patch` 전에는 가능하면 `scene inspect --with-values`를 먼저 실행해서 GameObject path와 field 이름을 확인한다.
- `prefab patch` 전에는 가능하면 `prefab inspect --with-values`를 먼저 실행해서 path와 field 이름을 확인한다. `remove-node`나 `remove-component` 같은 destructive op가 있으면 `--force`를 붙인다.
- inspect 응답이 클 때는 `--max-depth N`으로 깊이를 제한하고 `--omit-defaults`로 기본값을 생략한다. **`--with-values`에는 항상 `--omit-defaults`를 함께 붙여** 0/null/false/빈 값 기본값을 잘라낸다(컴포넌트당 30~55% 절약).
- `material info`도 `--omit-defaults`를 지원한다. URP/Lit 기준 48개 속성 → 변경된 것만 반환하여 토큰을 71% 절약한다.
- `--omit-defaults` 결과는 read-only이다. patch input으로 그대로 쓰면 생략된 필드가 복원되지 않는다.
- `SerializedProperty.propertyPath`는 추측하지 말고 inspect 결과를 기준으로 쓴다.
- live 편집 명령이 compile/update 중이면 읽기 전용 명령만 남기고 나머지는 재시도 흐름으로 본다.
- `PROTOCOL_MISMATCH`는 보통 CLI가 스스로 해결한다. 대상 프로젝트의 브릿지가 다른 wire protocol을 쓰면, CLI가 그 protocol을 말하는 설치본으로 자동 전환해 같은 명령을 다시 실행한다(에이전트가 할 일 없음). 그래도 이 에러가 돌아왔다면 해당 protocol을 말하는 CLI가 설치돼 있지 않다는 뜻이다. 재시도로 풀리지 않으므로, 사용자에게 **그 프로젝트의** Unity Editor에서 `Window > Unity CLI Manager > Install CLI`를 실행하도록 요청한다.
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

코드 수정 뒤 테스트 러너 기본 호출, `--failures-only`/`--wait`/`--no-domain-reload` 사용 기준, `refresh` 후 재실행 루프는 [references/command-flows.md](references/command-flows.md)의 `테스트 러너` 절을 따른다.

### Profile Workflow

성능 진단은 다음 루프를 따른다:

1. `unity-cli qa run-sequence --spec-json <spec> --profile`로 시퀀스를 실행하며 동시에 캡처한다. 응답의 `profileSummary`에 frame-time percentile, spike frame, hotspot, GC, CPU/GPU-bound verdict가 바로 들어온다.
2. 시퀀스 없이 캡처만 필요하면 `unity-cli profile capture start --frames <n>`으로 시작하고 `unity-cli profile capture stop --wait`로 요약이 나올 때까지 기다린다.
3. summary에서 이상 징후(overBudget count, spike, verdict)를 확인한 뒤 `unity-cli profile analyze <captureId> --gc` / `--frame <n>` / `--marker <name>` / `--spikes`로 로컬 sidecar를 드릴다운한다. Editor 왕복이 없어 반복 조회에 안전하다.
4. 원인을 찾아 코드/에셋을 수정하고, 같은 시퀀스를 다시 `--profile`로 실행해 새 캡처를 만든다.
5. `unity-cli profile compare <baseCaptureId> <headCaptureId>`로 수정 전후 캡처를 비교한다. verdict(`regression`/`improvement`/`unchanged`)와 frame-time/overBudget/GC delta, 마커별 regression·improvement가 한 번에 나온다. 판정 민감도는 `--threshold <percent>`(기본 5), 목록 길이는 `--limit <n>`으로 조절한다. 이것도 로컬 sidecar만 읽으므로 Editor가 꺼져 있어도 된다. 읽을 때 주의할 점:
   - 끝나지 않은 캡처(스크립트 재컴파일 등으로 중단된 캡처)는 비교하지 않고 `PROFILE_FAILED`로 거부한다. 이 에러가 나오면 그 쪽을 다시 캡처한다.
   - `deltaPercent`는 같은 객체의 `deltaPercentAvailable`이 `true`일 때만 의미가 있다. 기준값이 0이면(예: base의 overBudget 프레임이 0) 퍼센트가 정의되지 않으므로 절대값 `delta`만 본다.
   - 자기 시간(`deltaMs`)이 그대로여도 GC 할당이 늘어난 마커는 `regressions`에, 줄어든 마커는 `improvements`에 들어간다.
   - budget/Unity 버전/프레임 수가 다르면 `notes`에 경고가 붙으므로 그 경우 결과를 그대로 신뢰하지 않는다.
6. Edit Mode에서 카운터만 빠르게 보고 싶으면 `unity-cli profile stats --frames 30 --preset memory`처럼 프리셋(`frame`/`render`/`gc`/`memory`/`all`)을 사용한다.

**수치를 해석하기 전에 [references/profiling.md](references/profiling.md)를 읽는다.** 특히:
- 이 수치는 Editor 안에서 잰 것이라 절대값을 출시 성능으로 보고하면 안 된다 — 상대 비교 전용이다.
- hotspot 1위가 `Semaphore.WaitForSignal` / `WaitForTargetFPS` / `EditorLoop` / `Gfx.WaitFor*`면 그건 원인이 아니라 대기이거나 Editor 오버헤드다. 흔한 정상 상황이다.
- `gpuMedianMs: -1`은 "GPU 0ms"가 아니라 미측정 sentinel이다.
- `deltaPercent`가 커도 `deltaMs`가 프레임 예산 대비 무시할 크기면 노이즈다. Unity는 노이즈 임계값을 공표한 적이 없으므로 지어내지 않는다.
- `--budget-ms` 기본 16.67은 PC 60fps 가정이다. 모바일이면 30fps 기준 21.66ms가 Unity 권고값이다.
- 최적화를 제안하기 전에 `references/profiling.md`의 folklore 표를 확인한다 — `Camera.main` 캐싱, `foreach` boxing, `GetComponent` GC는 현재 Unity에서 전부 틀린 조언이다.

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
- 프로파일링 결과 해석 (무시할 마커, 노이즈 판단, 예산, folklore): [references/profiling.md](references/profiling.md)
- stale instance, busy, liveReachable, 로그 확인: [references/troubleshooting.md](references/troubleshooting.md)
- 빠르게 시작할 JSON 템플릿: `assets/`
