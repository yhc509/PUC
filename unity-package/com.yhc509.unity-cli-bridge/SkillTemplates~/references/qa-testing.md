# QA Test Automation

Play Mode에서 입력을 시뮬레이션하고 결과를 검증하는 흐름이다.

## 테스트 흐름

```
status → play → (입력 시뮬레이션) → 검증 (로그 + 스크린샷) → stop
```

1. `status --json`으로 `isCompiling: false` 확인
2. `play`로 Play Mode 진입 (자동으로 `runInBackground = true` 설정됨)
3. QA 커맨드로 입력 시뮬레이션
4. **로그 검증**: `read-console --type error --limit N` + `read-console --type log --limit N`
5. **시각 검증**: `screenshot --view game --path /tmp/qa-check.png` 후 이미지 확인
6. `stop`으로 Play Mode 종료 (`runInBackground` 원래값 복원됨)

## 입력 방식 선택

| 커맨드 | 동작 방식 | 사용 시점 |
|--------|-----------|-----------|
| `qa click --target <path>` | EventSystem.Execute | UI 버튼, 토글 등 클릭 |
| `qa click --qa-id <id>` | EventSystem.Execute | [QaTarget] 어트리뷰트로 마킹된 대상 |
| `qa ui-dump [--screenshot-width W --screenshot-height H]` | Bridge UI 구조 덤프 | 클릭 가능한 UI의 path/text/rect/center 조회 |
| `qa tap --x N --y N [--screenshot-width W --screenshot-height H]` | EventSystem raycast + Execute | 스크린샷 분석 결과 기반 UI 좌표 탭 |
| `qa swipe --target <path> --from x,y --to x,y` | Input System target-relative swipe | UI 슬라이더, ScrollRect 드래그 |
| `qa swipe --from x,y --to x,y [--screenshot-width W --screenshot-height H]` | Input System 스크린샷 좌표 | 스크린샷 분석 결과 기반 화면 스와이프 |
| `qa key --key <name>` | Input System 키보드 | 키 입력 시뮬레이션 |
| `qa wait --ms N` | CLI-side 대기 | 명령 사이 간격 |
| `qa wait-until --scene <name>` | Bridge 폴링 | 씬 전환 대기 |
| `qa wait-until --log-contains <text>` | Bridge 폴링 | 특정 로그 출력 대기 |
| `qa wait-until --object-exists <path>` | Bridge 폴링 | 오브젝트 존재 대기 |
| `qa wait-until --object-interactable <path>` | Bridge 폴링 | 버튼/대상 클릭 가능 대기 |
| `qa wait-until --object-gone <path>` | Bridge 폴링 | 로딩/오브젝트 비활성 또는 파괴 대기 |

## 입력 방식 판단 기준

### UI 클릭 → EventSystem 경로 (click)
- 버튼 클릭, 토글 등 EventSystem을 통해 즉시 처리하는 대상
- `--target`으로 hierarchy path 지정: `/Canvas[0]/Button[0]`
- EventSystem이 씬에 있어야 함 (없으면 `QA_NO_EVENT_SYSTEM` 에러)

### 클릭 가능 UI 탐색 → 구조화 덤프 (ui-dump)
- `qa ui-dump`는 현재 Play Mode 화면에서 `IPointerClickHandler`가 붙은 활성 GameObject를 수집한다.
- 응답의 `path`는 `qa click --target <path>`에 바로 사용할 수 있다.
- `path`는 고유할 때 신뢰할 수 있다. 같은 이름의 sibling이 같은 path를 공유하면 `centerX`/`centerY`로 `qa tap`을 사용한다.
- `centerX`/`centerY`는 스크린샷 이미지 좌표(top-left origin)이므로 `qa tap --x <centerX> --y <centerY>`에 바로 사용할 수 있다.
- `text`는 UGUI Text/TMP_Text 같은 자식 컴포넌트의 `text` 프로퍼티를 reflection으로 읽는다. TMP 패키지가 없어도 동작한다.
- `qa ui-dump` 자체는 raycast를 하지 않으므로 EventSystem을 요구하지 않는다. 실제 `qa click`과 좌표 기반 `qa tap --x/--y` 실행에는 EventSystem이 필요하다.

### UI 좌표 탭 → EventSystem raycast 경로 (tap)
- 스크린샷에서 읽은 좌표로 현재 화면의 UGUI 대상을 찾을 때 사용
- `screenshot` 응답의 `imageOrigin`은 `top-left`, `coordinateOrigin`은 `bottom-left`다. 일반적으로는 스크린샷에서 읽은 이미지 좌표 `(x, y)`를 그대로 `qa tap --x --y`에 넘기면 된다.
- `qa tap`은 top-origin 스크린샷 좌표를 받아 실행 시 Unity 화면 좌표계(bottom-origin)로 자동 변환한다.
- 최근 성공한 `screenshot` 캡처의 `width`/`height`를 기본값으로 사용해 현재 `Screen.width`/`Screen.height`에 맞게 자동 스케일링한다.
- Play Mode 진입/종료 때 자동 fallback 캐시는 리셋되므로 domain reload가 꺼져 있어도 이전 세션의 스크린샷 크기를 재사용하지 않는다.
- EventSystem이 좌표 위치를 raycast해 최상단 대상을 찾고, 클릭 핸들러가 자식에 걸려 있으면 상위 hierarchy에서 실제 `IPointerClickHandler`를 찾아 실행한다.
- 좌표 아래에 UI 대상이 없으면 `QA_TAP_NO_TARGET` 에러를 반환한다.

### UI 드래그 → target-relative Input System 경로 (swipe --target)
- `--target`으로 hierarchy path 지정: `/Canvas[0]/Scroll View[0]/Viewport[0]`
- target은 `RectTransform`이 있어야 함 (없으면 `QA_TARGET_NOT_RECT_TRANSFORM` 에러)
- `--from/--to`는 target `RectTransform` 중심 기준 **픽셀 오프셋**
- Bridge가 target-relative 오프셋을 화면 좌표로 변환한 뒤 multi-frame deferred swipe로 실행
- UGUI가 입력을 소비하려면 씬에 EventSystem이 있어야 함

### 비-UI 또는 제스처/키 입력 → Input System 경로 (swipe, key)
- 3D 오브젝트 터치, 화면 스와이프 제스처, 키보드 입력
- Input System 패키지 필요 (없으면 `QA_INPUT_SYSTEM_REQUIRED` 에러)
- 좌표 기반 `qa swipe`는 **스크린샷 기준 좌표계**를 사용한다.
- Y=0은 스크린샷 상단이며, Bridge가 실행 시 Unity 화면 좌표계(bottom-origin)로 자동 변환한다.
- 최근 성공한 `screenshot` 캡처의 `width`/`height`를 기본값으로 사용해 현재 `Screen.width`/`Screen.height`에 맞게 자동 스케일링한다.
- Play Mode 진입/종료 때 자동 fallback 캐시는 리셋되므로 domain reload가 꺼져 있어도 이전 세션의 스크린샷 크기를 재사용하지 않는다.
- 다른 기준 이미지 크기로 변환해야 할 때만 `--screenshot-width/--screenshot-height`를 함께 넘겨 명시적으로 덮어쓴다.
- 모든 `swipe`와 `key`는 deferred 또는 frame-based 입력 경로를 사용

## 사전 조건

### EventSystem
`qa click`, 좌표 기반 `qa tap --x/--y`, UGUI 대상 `qa swipe --target`은 씬에 EventSystem이 필요하다. 월드 대상 `qa tap --target`은 direct action 또는 Input System 좌표 fallback 경로를 사용하므로 EventSystem이 필수가 아니다.
없으면 메뉴로 추가:

```bash
# 먼저 메뉴 경로 확인
ucli execute-menu --list "GameObject/UI" --project "$P" --json

# EventSystem 추가
ucli execute-menu --path "GameObject/UI (Canvas)/Event System" --project "$P" --json
```

### Input System
`qa key`와 모든 `qa swipe`는 Input System 패키지가 필요하다.
미설치 시 Bridge가 정상 동작하되, 해당 커맨드만 `QA_INPUT_SYSTEM_REQUIRED` 에러를 반환한다.

### Play Mode
모든 QA 커맨드는 Play Mode에서만 실행 가능하다. Play Mode가 아니면 `QA_NOT_PLAYING` 에러.

## 테스트 UI 구성

`execute-menu`로 Unity 기본 메뉴를 사용해 UI를 추가한다. 커스텀 커맨드 불필요.

```bash
P="$(pwd -P)"

# 메뉴 경로 조회 (Unity 버전마다 다름)
ucli execute-menu --list "GameObject/UI (Canvas)" --project "$P" --json

# Canvas + EventSystem + Button 추가
ucli execute-menu --path "GameObject/UI (Canvas)/Canvas" --project "$P" --json
ucli execute-menu --path "GameObject/UI (Canvas)/Event System" --project "$P" --json
ucli execute-menu --path "GameObject/UI (Canvas)/Button - TextMeshPro" --project "$P" --json
```

커스텀 스크립트가 필요하면 `execute --code`로 C# 코드를 실행한다. `using` 지시문을 자유롭게 사용 가능:

```bash
ucli execute --force --project "$P" --code '
using UnityEngine.UI;
// 코드 본문
'
```

## 검증 방법

### 로그 검증
`read-console`로 Debug.Log 출력과 에러를 확인한다.

```bash
ucli read-console --type log --limit 5 --project "$P" --json
ucli read-console --type error --limit 5 --project "$P" --json
```

### 시각 검증 (스크린샷)
`screenshot --view game`으로 Game View를 캡처해서 시각적 변화를 확인한다.

```bash
ucli screenshot --view game --path /tmp/qa-check.png --project "$P" --json
ucli screenshot --view game --format jpg --quality 70 --max-width 1024 --path /tmp/qa-check.jpg --project "$P" --json
# 이후 이미지를 Read 도구로 확인
```

스크린샷 좌표 기반 입력과 좌표 변환 규칙은 아래 `Coordinate-Based UI Interaction` 섹션을 참고한다.

**제한사항**: Play Mode에서는 `ScreenCapture.CaptureScreenshotAsTexture()`를 사용하므로 ScreenSpaceOverlay Canvas(UGUI)까지 포함해 캡처된다. 비 Play Mode에서는 `Camera.Render()` fallback을 사용하므로 ScreenSpaceOverlay Canvas는 포함되지 않는다.

검증 순서는 **로그 먼저, 스크린샷은 보조**로 사용한다. 로그가 이벤트 발생을 확인하는 1차 수단이고, 스크린샷은 시각적 상태를 보조적으로 확인한다.

## Coordinate-Based UI Interaction

### When to use `qa ui-dump`, `qa click`, or `qa tap`
- **`qa ui-dump`**: Use first when you need clickable UI candidates with path, visible text, and image-space rect/center.
- **`qa click --target <path>`**: Use when you know the GameObject path from `qa ui-dump` or `scene inspect`. 100% precise, no coordinate guessing.
- **`qa tap --x <X> --y <Y>`**: Use when you only have image coordinates, such as `centerX`/`centerY` from `qa ui-dump` or pixels read from a screenshot.

### qa ui-dump Workflow
1. Enter Play Mode and, if you already captured a reference image, keep its `width`/`height` available
2. Dump candidates: `qa ui-dump` -> inspect each element's `path`, `text`, `interactable`, `x`, `y`, `width`, `height`, `centerX`, `centerY`
3. Prefer path click: `qa click --target <path>`
4. If a coordinate tap is better, use `qa tap --x <centerX> --y <centerY>`

Returned `path` values are reliable when unique; if same-named siblings share a path, use `centerX`/`centerY` with `qa tap` instead.

If the dump should match a resized image, pass `--screenshot-width` and `--screenshot-height` together. Otherwise, the bridge uses the last successful `screenshot` dimensions when available.

### Non-UI world objects (`qa world-dump`)

UI elements come from `qa ui-dump`. For non-UI world objects (units on a battle grid, world-space interactables), the game opts them in:

- Implement `UnityCliBridge.Bridge.IQaTappable` on your own component, or attach the `QaTappable` marker component (set `label`, optional `anchor`, optional `onQaTap`).
- `qa world-dump` then lists them with `label`, `path`, image-space `centerX`/`centerY`, `onScreen`, `hasAction`.
- Tap with `qa tap --target <path>`. If the object's `TryQaTap()` handles it (e.g. `onQaTap` is wired), the bridge invokes it directly; otherwise the bridge simulates an Input System tap at the anchor, which reaches games that poll the Input System directly (where `qa tap --x --y`'s EventSystem path does not).
- Off-screen objects are hidden unless you pass `--include-offscreen`.
- Same-named sibling world objects share a path and resolve to the first match; give tappable objects unique names/labels to target them individually.

### qa tap Workflow
1. Take a screenshot: `screenshot` -> note `width`, `height`, and when needed `screenWidth`, `screenHeight`, `imageOrigin`, `coordinateOrigin` from the response
2. Analyze the screenshot image to find the target pixel coordinates `(X, Y)` with `Y=0` at the top-left
3. Tap: `qa tap --x <X> --y <Y>` -> pass the screenshot image coordinates directly; the bridge automatically reuses the last screenshot dimensions for coordinate conversion
4. Verify: `read-console` to check whether the expected log or event fired

The bridge automatically:
- Accepts screenshot image coordinates as-is, so you do not pre-flip Y or pre-scale for DPI/resolution differences
- Inverts the Y axis (screenshot top-origin -> Unity bottom-origin)
- Scales coordinates if the screenshot resolution differs from `Screen.width` / `Screen.height` (common in the Editor because of DPI)
- Resets cached screenshot dimensions when Play Mode enters or exits, so stale sizes from a previous session are not reused when domain reload is disabled

### Explicit screenshot dimensions
If you need to specify different dimensions, for example when the screenshot was resized. If you omit them, the bridge uses the last successful `screenshot` size automatically:

```bash
ucli qa tap --x 480 --y 225 --screenshot-width 961 --screenshot-height 554 --project "$P" --json
```

### Precise coordinate lookup fallback via execute
Prefer `qa ui-dump` for clickable UI. When screenshot coordinates are imprecise and the target is not exposed by `IPointerClickHandler`, use `execute` to log exact UI element positions:

```bash
# Find a button's screen center coordinates
ucli execute --file coords.cs --force --project "$P" --json
```

Where `coords.cs` contains:

```csharp
using UnityEngine;

var btn = GameObject.Find("ButtonName");
var rt = btn.GetComponent<RectTransform>();
var corners = new Vector3[4];
rt.GetWorldCorners(corners);
Vector3 center = (corners[0] + corners[2]) * 0.5f;
// For ScreenSpaceOverlay canvas, world coords = screen coords
Debug.Log($"Screen center: {center.x},{center.y} Screen={Screen.width}x{Screen.height}");
```

Then reverse-calculate to screenshot coordinates:
- `imageX = screenX * screenshotWidth / Screen.width`
- `imageY = screenshotHeight - (screenY * screenshotHeight / Screen.height)`

### Coordinate system summary
| Space | Y=0 | Used by |
|-------|-----|---------|
| Screenshot/Image | Top | `qa tap` input coordinates |
| Unity Screen | Bottom | Internal Unity coordinates |

The bridge converts automatically when screenshot dimensions are known.

## 예시: 버튼 클릭 테스트

```bash
P="$(pwd -P)"

ucli play --project "$P" --json
sleep 1

ucli qa click --target "/Canvas/Button" --project "$P" --json

# 로그 검증
ucli read-console --type log --limit 5 --project "$P" --json

# 시각 검증
ucli screenshot --view game --path /tmp/qa-click.png --project "$P" --json

ucli stop --project "$P" --json
```

## 예시: 조건 대기 후 검증

```bash
ucli play --project "$P" --json
sleep 1

# 특정 씬으로 전환될 때까지 대기 (최대 10초)
ucli qa wait-until --scene GameScene --timeout 10000 --project "$P" --json

# 전환 후 입력 시뮬레이션
ucli qa click --target "/Canvas/StartButton" --project "$P" --json
ucli qa wait-until --log-contains "Game started" --timeout 5000 --project "$P" --json

ucli stop --project "$P" --json
```

고정 대기 대신 화면 상태를 조건으로 기다리면 불필요한 sleep을 줄일 수 있다. 여러 조건을 함께 지정하면 모두(AND) 충족돼야 한다. 버튼은 활성화와 유효 interactable 상태를 함께 기다리고, 로딩 표시는 active hierarchy에서 사라질 때까지 기다린다. `--object-interactable`은 대상 컴포넌트의 `IsInteractable()` 결과를 우선 사용하고, 없으면 public bool `interactable` 프로퍼티를 읽으며, 해당 상태가 없는 오브젝트는 active로 resolve되면 true로 취급한다.

```bash
# 시작 버튼이 클릭 가능해질 때까지 대기 후 클릭
ucli qa wait-until --object-interactable StartButton --timeout 10000 --project "$P" --json
ucli qa click --qa-id StartButton --project "$P" --json

# 로딩 스피너가 비활성/파괴될 때까지 대기
ucli qa wait-until --object-gone LoadingSpinner --timeout 10000 --project "$P" --json
```

## 알려진 제한사항

- **좌표 기준**: `qa swipe --target`의 `--from/--to`는 절대 화면 좌표가 아니라 target `RectTransform` 중심 기준 픽셀 오프셋이다.
- **스크린샷 좌표 변환**: `qa tap`과 좌표 기반 `qa swipe`는 스크린샷 기준 top-origin 좌표를 입력받고, 실행 시 bottom-origin Unity 화면 좌표로 변환된다.
- **스크린샷**: Play Mode에서는 `ScreenCapture` 기반으로 Overlay Canvas까지 캡처된다. 비 Play Mode에서는 `Camera.Render()` fallback이라 ScreenSpaceOverlay Canvas는 포함되지 않는다.
- **using 제한**: `execute --code`의 using 추출은 `using Namespace;`, `using static Namespace.Type;`, `using Alias = Namespace.Type;`를 지원한다. 단, `using Dict = Dictionary<string, int>;` 같은 generic type alias는 지원하지 않는다.
- **Game View 갱신**: `play` 커맨드가 `runInBackground = true`를 자동 설정하므로, Unity가 포커스를 잃어도 Game View가 갱신된다. `stop` 시 원래값으로 복원된다.

## 트러블슈팅

| 에러 코드 | 원인 | 해결 |
|-----------|------|------|
| `QA_NOT_PLAYING` | Play Mode가 아님 | `play` 먼저 실행 |
| `QA_NO_EVENT_SYSTEM` | EventSystem 없음 | `execute-menu`로 추가 |
| `QA_TAP_NO_TARGET` | 좌표 아래에 클릭 가능한 UI가 없음 | 좌표/해상도 기준 확인, UI가 해당 위치에 표시되는지 점검 |
| `QA_INPUT_SYSTEM_REQUIRED` | Input System 미설치 | `package add --name com.unity.inputsystem` |
| `QA_TARGET_NOT_FOUND` | hierarchy path 틀림 | `scene inspect`로 정확한 path 확인 |
| `QA_TARGET_NOT_RECT_TRANSFORM` | `qa swipe --target` 대상이 UI `RectTransform`이 아님 | UI hierarchy path로 다시 지정 |
| `QA_NO_POINTER_DEVICE` | Mouse/Touchscreen 없음 | Input System 디바이스 확인 |
| `QA_WAIT_TIMEOUT` | 조건 미충족 타임아웃 | 조건 확인, timeout 늘리기 |
| `BUSY` | Unity 컴파일/업데이트 중 | 컴파일 완료 후 재시도 |
