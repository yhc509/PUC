# Profiling

`profile` 명령군의 출력을 **해석**하는 법. 명령 사용법은 SKILL.md의 Profiling Workflow를 보고, 여기서는 "이 숫자를 믿어도 되는가"만 다룬다.

## 0. 먼저 알아야 할 한계 — 이건 Editor 안에서 잰 수치다

`profile capture`는 Unity Editor 프로세스 안에서 돈다. Unity 공식 입장:

> Editor를 프로파일링 대상으로 쓰면 정확도에 큰 영향이 있다. **Profiler 창이 사실상 자기 자신을 재귀적으로 프로파일링하고 있다.**
> — Ultimate guide to profiling Unity games (Unity 6 edition)

따라서:

- **절대값을 출시 성능으로 보고하지 마라.** "이 게임은 60fps가 안 나온다" 같은 결론을 이 데이터로 내리면 안 된다.
- **상대 비교로만 써라.** 같은 프로젝트, 같은 시나리오에서 수정 전후를 비교하는 용도다. 그래서 `profile compare`가 이 명령군의 핵심이다.
- 방향은 항상 **과대평가**다. Editor는 일을 더 하지, 덜 하지 않는다. 다만 **얼마나** 부풀려지는지는 Unity가 어디에도 수치로 공표하지 않았다. "1.5배" 같은 비율을 지어내지 마라.
- 특히 못 믿을 것: **메모리**(Editor가 텍스처를 강제로 read/write 활성화 → 실제보다 큼), **fill rate**(해상도가 타깃 기기와 다름), **스크립트 비용**(Editor는 Mono, 빌드는 IL2CPP인 경우가 많음), **VSync 거동**(Editor는 GPU VSync를 안 하고 `WaitForTargetFPS`로 흉내낸다).

Unity가 승인하는 사용 패턴이 정확히 이 도구의 자리다: 빌드에서 문제를 찾고 → Editor에서 빠르게 반복 수정하고 → 다시 빌드로 확인.

## 1. 무시해야 할 마커 — 가장 흔한 오진

hotspot 1위나 spike의 `topMarker`가 아래 목록에 있으면 **그건 원인이 아니다.** 대기(idle)거나 Editor 오버헤드다.

Unity 자신이 Highlights 모듈에서 "CPU Active Time"을 계산할 때 아래 세 개를 **빼고** 센다 — 즉 Unity 공식 기준으로도 이건 일이 아니다.

| 마커 | 실제 의미 | 어떻게 읽나 |
|---|---|---|
| `WaitForTargetFPS` | 유휴. 목표 프레임레이트까지 남는 시간을 놀고 있는 것 | **크면 좋은 것이다.** 예산 안에 들어왔다는 뜻. 단 `Gfx.WaitForPresentOnGfxThread` 하위에 있으면 GPU 대기 |
| `Gfx.WaitForPresentOnGfxThread` | 메인 스레드가 렌더 스레드를 기다림 | 이것만으로 GPU-bound 단정 금지. 2절 참조 |
| `Gfx.WaitForGfxCommandsFromMainThread` / `Gfx.WaitForCommands` | 렌더 스레드가 놀고 있음 | **메인 스레드 병목** 신호 |
| `Semaphore.WaitForSignal` | 스레드 동기화 대기 | Unity 스태프: "`…WaitFor…` 샘플은 결코 범인이 아니다. 느린 곳이 다른 데 있다는 뜻일 뿐" |
| `Gfx.WaitForRenderThread` | 메인 스레드 대기 | 비용은 렌더 스레드에 있다 |
| `<API>.WaitForLastPresent` | 메인 스레드가 GPU flip 대기 | 유휴 |
| `EditorLoop` | **Editor 자체 비용. 빌드에 존재하지 않는다** | 프레임 예산 계산에서 빼라. spike 원인이 `EditorLoop`면 Editor 아티팩트다 |
| `EditorOnly ...` 접두 | Editor 전용 검사(prefab, consistency 등) | 안전하게 무시. 여기 딸린 GC bytes도 Editor 아티팩트 |
| `Mono.JIT` | 첫 호출 시 JIT 컴파일 | Editor는 Mono. IL2CPP 빌드엔 없다 |
| `Profiler.CollectEditorStats` | 프로파일러 자기 비용 | 무시 |

반대로 **`GC.Collect`는 진짜 일이다.** 이건 실제 정지 시간이고 무시하면 안 된다.

주의: `Semaphore.WaitForSignal`이 hotspot 1위로 뜨는 건 이 도구에서 **흔한 정상 상황**이다. 실측 예로 샘플 프로젝트의 idle 씬 캡처에서 `selfTotalMs: 35.4`로 1위였고, 2위가 `EditorLoop`였다. 둘 다 게임 코드가 아니다.

## 2. CPU-bound / GPU-bound 판정 읽기

`verdict` 필드:

| 필드 | 값 | 의미 |
|---|---|---|
| `bound` | `cpu` / `gpu` / `withinBudget` / `unknown` | `withinBudget`이면 최적화해도 프레임레이트가 안 오른다. 멈춰라 |
| `basis` | 판정 근거 | `none`이면 GPU 측정이 없어 마커 신호로만 추정했다는 뜻 |
| `gpuMedianMs` | 수치 또는 `-1` | **`-1`은 "GPU 0ms"가 아니라 미측정 sentinel이다.** 0으로 읽지 마라 |

`gpuMedianMs: -1`이면 GPU-bound 여부를 이 캡처만으로 확정할 수 없다. `bound`는 마커 기반 추정이다.

Unity의 판정 기준(예산 33.33ms 기준):

- CPU 25 / GPU 20 → CPU-bound지만 **예산 안**. "문제 없음. 최적화해도 프레임레이트 안 오른다"
- CPU 40 / GPU 20 → CPU-bound. GPU 최적화는 도움 안 됨
- CPU 20 / GPU 40 → GPU-bound
- CPU 40 / GPU 40 → 양쪽 다

핵심 원칙: **가장 오래 걸리는 칩/스레드가 성능을 결정한다.** 거기만 손대라.

## 3. 노이즈 플로어 — 언제 delta를 믿지 마라

**Unity는 "이 ms 미만은 노이즈"라는 임계값을 공표한 적이 없다.** 지어내지 마라. 공표된 가드레일은 Performance Testing 패키지의 두 문장뿐이다:

> 1ms 미만 측정은 피하라. 불안정한 환경에 더 민감하다.
> 데이터셋 편차가 5% 미만일 때 안정적이라고 본다.

그래서 `profile compare` 결과를 읽을 때:

- **`deltaPercent`가 크다고 중요한 게 아니다.** `deltaMs`의 절대 크기를 먼저 봐라. 실측 예: `RenderLoop.Sort`가 `deltaMs: 0.0000625`, `deltaPercent: 75.3`으로 regression 1위로 올라온 적이 있다. 75% 회귀처럼 보이지만 0.00006ms다. 의미 없다.
- **프레임 예산 대비 비율로 환산해라.** 16.67ms 예산에서 0.1ms 변화는 0.6%다. 이게 판단 기준이어야 한다.
- **표본 크기를 확인해라.** `capturedFrames`가 작으면 median이 안정적이지 않다. Unity가 실무에서 쓰는 캡처 크기는 300프레임(10초) ~ 2000프레임이다.
- `notes`에 "프레임 수 차이가 큽니다"가 있으면 **비교 자체를 신뢰하지 마라.** 다시 떠라.
- 계측 오버헤드는 마커 수에 비례해서 **변한다**(고정값이 아니다). 호출이 많은 코드는 실제보다 비싸 보인다.

## 4. GC는 bytes로만 판단한다

`profile`은 GC를 바이트로만 내보내고 ms는 절대 내보내지 않는다. 이유는 Unity 공식 문서에 있다:

> `GC.Alloc` 마커는 Begin/End로 시간을 재지 않는다. 오버헤드를 줄이려고 타임스탬프와 할당 크기만 기록한다. **Profiler가 표시하는 지속시간은 뷰에서 보이게 하려고 붙인 인위적 값이다.**

즉 `GC.Alloc`의 self time으로 순위를 매기면 안 된다. 다른 도구에서 그 숫자를 보더라도 무시해라.

목표치는 Unity가 명확히 말한다: **주 게임 루프에서 프레임당 0 bytes**, 최대한 0에 가깝게.

**중요한 함정 — spike 목록에 GC가 없다고 안심하지 마라.** Unity 기본값인 Incremental GC는 수집을 여러 프레임에 분산하고, 프레임 끝 유휴 시간을 쓴다. 그래서 할당 압박이 `GC.Collect` 스파이크로 안 보일 수 있다. **프레임당 bytes가 선행 지표고, `GC.Collect` ms는 후행이며 안 보일 수도 있는 지표다.**

Incremental GC 자체도 공짜가 아니다 — write barrier 때문에 프레임당 최대 ~1ms의 스크립팅 오버헤드가 붙는다. 할당이 0이면 애초에 Incremental GC가 필요 없다.

## 5. `--budget-ms` 기본값은 PC 60fps 가정이다

기본값 `16.67`은 60fps 기준이다. **모바일 프로젝트면 이 값을 그대로 쓰면 안 된다.**

Unity가 유일하게 숫자로 공표한 헤드룸 가이드:

> 열 문제 대응을 위해 약 **35% 유휴 시간**을 남겨라.

| 목표 | 단순 계산 | 모바일 권장(35% 헤드룸) |
|---|---|---|
| 30 fps | 33.33 ms | **21.66 ms** |
| 60 fps | 16.67 ms | **10.83 ms** |
| Quest 2 (72 fps) | 13.88 ms | — |

Unity 자신이 "모바일 60fps는 많은 기기에서 달성하기 어렵고 배터리를 2배로 먹는다. 그래서 많은 모바일 게임이 30fps를 목표로 한다"고 말한다.

`--budget-ms`는 `overBudgetFrames`와 spike 판정, verdict의 `withinBudget`을 좌우한다. 잘못 잡으면 결론 전체가 낙관적으로 기운다. **프로젝트 타깃을 모르면 사용자에게 물어라.**

## 6. 첫 프레임은 버려라

Play Mode 진입 직후 프레임은 체계적으로 느리다. 원인은 전부 문서화돼 있다:

- Play Mode 진입 / 도메인 리로드
- `Mono.JIT` — 함수 첫 호출 시 컴파일
- 셰이더 variant 컴파일 및 GPU 업로드 (Editor는 `Library/ShaderCache` 미스면 **컴파일까지** 한다)
- 씬·에셋 로딩

실측: 샘플 캡처의 유일한 spike가 프레임 1(34.6ms)이었고 원인 마커는 `Semaphore.WaitForSignal`이었다. 시작 비용이지 성능 문제가 아니다.

Unity의 Performance Testing 패키지 기본 warmup은 **80ms 또는 최소 3프레임**이다. 인용 가능한 유일한 선례가 이것이다. 그보다 많이 버리는 건 판단이며, 판단이라고 밝혀라.

셰이더/머티리얼을 수정한 직후 첫 실행은 그 다음 실행과 비교 불가다. `profile compare` 전에 한 번 예열해라.

## 7. `profile compare` 사용 규율

Unity의 A/B 절차를 그대로 따른다:

1. **재현 가능한 시나리오를 정한다.** Unity: "스크립트로 짜거나 반복 가능한 수동 플레이가 가장 좋다 — 무작위 부작용을 최소화한다." → `qa run-sequence --profile`이 이걸 위한 명령이다.
2. 수정 전 캡처
3. **한 번에 하나만** 바꾼다
4. 수정 후 캡처
5. `profile compare <before> <after>`

읽는 순서:

| 필드 | 확인할 것 |
|---|---|
| `notes` | **여기부터 본다.** 비어 있지 않으면 비교 신뢰도가 떨어진 것 |
| `verdict` | median frame time 기준 판정 |
| `deltaPercentAvailable` | `false`면 기준값이 0이라 퍼센트가 정의되지 않는다. **`deltaPercent`를 읽지 말고 `delta`만 봐라** |
| `frameTimeWorstMs` | median이 좋아져도 worst가 나빠질 수 있다. hitch는 worst가 만든다 |
| `gcBytesTotal` | 시간이 그대로여도 할당이 늘었으면 회귀다 |
| `regressions` | `deltaMs` 절대값을 예산 대비로 환산해서 판단 (3절) |
| `truncated` | `true`면 목록이 잘렸다. `--limit`을 올려 다시 |

**끝나지 않은 캡처는 비교되지 않는다.** 캡처 도중 도메인 리로드가 나면 `status = "Interrupted"` sidecar가 남고, `profile compare`는 이걸 `PROFILE_FAILED`로 거절한다. 조용히 "100% 개선"으로 읽히는 걸 막는 가드다. 이 에러를 보면 다시 캡처해라.

## 8. 마커별 원인과 처방

**Unity 공식 마커 레퍼런스에 문서화된 것만** 아래에 싣는다. 캡처에는 문서화되지 않은 이름(`RenderLoop.Sort`, `Render.Mesh`, `Shadows` 계열 등)도 뜨는데, **그 의미를 단정하지 마라.** 출처가 없다.

| 마커 | 의미 | 처방 |
|---|---|---|
| `BehaviourUpdate` | 모든 `MonoBehaviour.Update` | 원인이 안 보이면 **추측하지 말고 데이터를 더 모아라** — Unity 권고는 "Profiler Marker를 코드에 추가하거나 deep profiling" |
| `Update.ScriptRunBehaviourUpdate` | 위 + 코루틴 | 〃 |
| `PreLateUpdate.ScriptRunBehaviourLateUpdate` | 모든 `LateUpdate` | 〃 |
| `FixedBehaviourUpdate` | 모든 `FixedUpdate` | 아래 physics 항목 참조 |
| `Physics.Processing` / `Physics.Simulate` | 물리 시뮬레이션 대기/준비 | **호출 수가 10에 가까우면 경고 신호.** 무거운 프레임 때문에 물리가 따라잡으려 여러 번 도는 죽음의 나선이다. 물리 문제가 아니라 *증상*일 수 있다. `Maximum Allowed Timestep`(Project Settings > Time) 확인, fixed timestep 빈도 낮추기 |
| `Physics.ProcessReports` | `OnTriggerEnter` 등 콜백 전달 | 비용은 시뮬레이션이 아니라 **당신의 콜백**에 있다 |
| `Physics.Processing`이 높은데 물리 오브젝트가 거의 없음 | job stealing으로 다른 시스템 작업이 물리로 집계됨 | 오진 주의 |
| `Camera.Render` (메인 스레드) | 카메라당 처리 시간 | **활성 카메라 수를 줄여라.** 아무것도 안 그리는 카메라를 추가해도 비용이 는다. 분할화면이 아니면 활성 카메라는 하나여야 한다 |
| `Camera.Render` (**렌더 스레드**) | — | **CPU-bound 신호다.** draw call이나 텍스처 전송에 시간을 너무 쓰고 있다 |
| `Canvas.BuildBatch` | UI 재배칭 | 한 Canvas에 Canvas Renderer가 과도하게 많다 → **Canvas를 분할** |
| `Canvas.SendWillRenderCanvases` | UI 레이아웃·그래픽 리빌드 | Layout 컴포넌트가 많으면 RectTransform으로 대체. 매 프레임 도는데 hotspot이 없으면 동적 요소와 정적 요소가 한 Canvas에 섞인 것 → 분할 |
| `Text_OnPopulateMesh` | 텍스트 메시 생성 | Best Fit이 켜져 있으면 끈다. Unity Learn: "UI Text의 Best Fit은 일반적으로 절대 쓰면 안 된다" |
| `Animators.*` / `Director.PrepareFrame` | 애니메이션 | 같은 Transform 계층에 Animator 여럿 두지 마라(병렬화 불가). `OnStateMachineEnter/Exit`를 구현한 StateMachineBehaviour는 메인 스레드로 강제된다. scale 커브는 translation/rotation보다 비싸다 |
| `GC.Alloc`이 한 마커에 집중 | 할당 출처 | Call Stacks 토글 + Calls 드롭다운으로 출처 추적(2019.3+, deep profiling 불필요) |
| `Resources.Load`가 게임플레이 중 | 동기 로딩 | Addressables로 이전. Resources는 언로드 전까지 메모리에 남는다 |

**GPU-bound일 때 처방 순서** (Unity의 3분류):
1. **Fill rate 한계** — overdraw 줄이기(투명 UI/파티클/스프라이트 중첩), fragment 셰이더 비용, Dynamic Resolution
2. **메모리 대역폭 한계** — 밉맵 켜기, 압축 포맷
3. **Vertex 처리 한계** — 삼각형 수, UV seam/하드 엣지, LOD

**CPU-bound일 때 처방 순서** (Unity가 제시한 순서 그대로): 물리 → 스크립트 Update → GC 할당/수집 → 카메라 컬링·렌더링 → draw call 배칭 → UI 리빌드 → 애니메이션

## 9. 카운터(`profile stats`)에는 절대 임계값이 없다

Unity는 draw call, SetPass, batch, triangle, 메모리 어느 것에도 **"이 값을 넘으면 문제"라는 수치를 공표하지 않았다.** "모바일 draw call 200 이하" 같은 숫자는 전부 커뮤니티 관행이다. 사실처럼 말하지 마라.

Unity가 선을 긋는 곳은 딱 두 군데다: **GC 할당(프레임당 0 목표)**과 **프레임 예산**.

따라서 카운터는 **프로젝트 자체 baseline 대비 변화량**으로만 판단해라.

| 지표 | 뜻 | 비고 |
|---|---|---|
| SetPass Calls | 셰이더 패스 전환 횟수 | **셋 중 가장 의미 있는 수치.** render state 변경이 그래픽 API에서 가장 비싼 작업이다 |
| Draw Calls | 발행한 draw call 총수 | |
| Batches | 처리한 배치 수 | |
| `CPU Total Frame Time` | 프레임 간 전체 시간 | **대기 시간 포함.** 작업량 지표로 쓰지 마라 |

Editor 캡처에서만 존재하는 카운터: Texture/Mesh/Material/AnimationClip Count, GC Allocated In Frame — 릴리즈 빌드엔 아예 없다. 그리고 Editor는 텍스처를 강제 read/write하므로 **텍스처 메모리는 체계적으로 부풀려져 있다.**

## 10. 추천하면 안 되는 조언 (Unity가 부정한 folklore)

아래는 널리 퍼졌지만 **현재 Unity 버전에서 틀렸다.** 최적화 제안에 넣지 마라.

| 흔한 조언 | 실제 |
|---|---|
| `Camera.main`은 매번 태그 검색 → 반드시 캐싱 | **2019.4.9부터 캐시됨.** 공식 문서: "`GetComponent` 호출과 비슷한 작은 CPU 오버헤드". 태그 검색도 GC도 아니다. 캐싱은 여전히 권장이지만 이유가 훨씬 약하다 |
| `foreach`가 enumerator를 boxing | **Unity 5.5 컴파일러 교체로 해결.** 배열과 구체 타입 `List<T>`/`Dictionary<K,V>`는 안전. `IEnumerable<T>` 타입 변수로 순회할 때만 여전히 boxing |
| `GetComponent<T>()`는 GC 발생 | **빌드에선 발생 안 함.** Editor에서, 그것도 *못 찾았을 때만* fake-null 래퍼 할당. `TryGetComponent`로 그것도 회피 |
| draw call을 줄여라 | **URP/HDRP에선 초점이 틀렸다.** Unity는 static batching과 GPU Instancing 체크박스를 *끄고* SRP Batcher + GPU Resident Drawer를 쓰라고 한다 — 이건 draw call이 아니라 **state 변경**을 줄인다. Built-in용 조언이 여기선 뒤집힌다 |
| dynamic batching으로 draw call 절감 | Unity: "대부분의 용도에서 더 이상 권장하지 않는다. CPU 오버헤드가 draw call 오버헤드보다 클 수 있다." HDRP 미지원 |
| 여러 `Update`를 매니저 하나로 합쳐라 | **Unity 공식 출처가 없다.** 널리 퍼졌을 뿐. Unity 권고는 "마커를 추가하거나 deep profile하라"지 구조 변경이 아니다 |
| `RaycastNonAlloc`은 deprecated | **2D만 그렇다.** 3D `Physics.RaycastNonAlloc`은 Unity 6.3 매뉴얼에서 여전히 권장 패턴이다 |

반대로 **여전히 유효한** 할당 원인: 문자열 연결/보간, boxing(Unity GC는 generational이 아니라 더 아프다), hot path의 LINQ, 상태를 캡처하는 클로저/람다, Unity API가 반환하는 배열(매번 새 복사본), `new WaitForSeconds`, `params` 배열.

## 11. 메모리 릭 추세 감시 (`profile memory`)

프레임 시간이 아니라 **메모리**를 볼 때 쓴다. 스냅샷 없이 카운터 median만으로 추세를 잡는 경량 경로이고, Unity 스태프가 권하는 순서(카운터 추세 → 필요할 때만 스냅샷)를 그대로 따른다.

```bash
unity-cli profile memory                          # baseline reportId
# 의심 플로우 재현 (플레이, 씬 전환 반복, 시간 경과)
unity-cli profile memory                          # head reportId
unity-cli profile memory compare <base> <head>
```

읽는 법:

- **verdict는 `Total Used Memory` median 하나로 정해진다.** `--threshold`(기본 5%)를 넘게 늘면 `regression`, 그만큼 줄면 `improvement`. 나머지 카운터는 *왜* 그런지 설명하는 재료다.
- `increases`에서 **Count와 Memory가 같이 오르는 asset-type**을 먼저 봐라. `Texture Count` + `Texture Memory` 동반 상승 = 텍스처가 해제되지 않는다는 뜻이다. Memory만 오르고 Count가 그대로면 개별 에셋이 커진 것(해상도·포맷 변경)이다.
- `GC Used Memory` 상승은 managed 객체가 남아 있다는 뜻이고, `GC Reserved Memory`만 오르는 건 힙이 확장된 것으로 릭이 아닐 수 있다. 둘을 구분해라.
- **`Total Reserved`/`System Used`는 릭 판정에 쓰지 마라.** 예약 메모리는 반환되지 않은 채 유지되는 게 정상이다.
- 같은 모드끼리 비교해라. editmode ↔ playmode 비교는 Play 진입 자체가 수백 MB를 움직이므로 의미가 없고, 그 경우 `notes`에 mode 불일치 경고가 붙는다 — 경고가 보이면 결과를 신뢰하지 마라.
- `deltaPercentAvailable`이 false면 퍼센트는 무시하고 절대 `delta`만 봐라. verdict도 그때는 `unchanged`로 고정된다.
- `unavailable` 배열은 그 Unity 버전에 없는 카운터일 뿐 실패가 아니다. 다만 **`Total Used Memory`가 거기 있으면 verdict가 무의미**하므로 그때는 개별 카운터로만 판단해라.
- Editor 수치라는 §0의 한계가 여기에도 그대로 적용된다. 특히 Editor는 텍스처를 강제 read/write하므로 텍스처 메모리는 부풀려져 있다 — 절대값이 아니라 변화량만 봐라.

추세가 나쁠 때만 정밀 단계로 간다:

```bash
unity-cli profile memory snapshot     # → .snap 경로
```

`com.unity.memoryprofiler`가 필요하고(없으면 설치 안내와 함께 거부), profile capture와 동시에 실행되지 않는다. 분석은 **Window > Analysis > Memory Profiler**에서 하고, CLI는 `.snap`을 파싱하지 않는다. 파일은 에디터 메모리만큼 커지며(1GB 초과가 흔하다) 자동 삭제되지 않으니 다 쓰면 지워라.

## 12. headless 회귀 파이프라인

아래는 전부 사람 개입 없이 돈다 — GUI 에디터도, 포커스도 필요 없다.

```bash
unity-cli editor launch --project <path>     # 기본 headless (GPU는 살아 있음)
unity-cli play
unity-cli profile capture start --duration 10
unity-cli profile capture stop --wait        # → head captureId
unity-cli profile memory                     # → head reportId
unity-cli stop
unity-cli editor stop
# 여기서부터는 Editor 없이 로컬 연산:
unity-cli profile compare <baseCapture> <headCapture>
unity-cli profile memory compare <baseReport> <headReport>
```

known-good 실행의 captureId/reportId를 baseline으로 보관해라. sidecar는 `Library/com.yhc509.unity-cli-bridge/` 아래에 남아 에디터를 껐다 켜도 유지된다.

주의: Play Mode 진입과 패키지 설치는 도메인 리로드를 일으켜 IPC 소켓을 잠깐 끊는다. 그 직후 명령이 `LIVE_UNAVAILABLE`(retryable)로 실패하면 몇 초 뒤 재시도해라 — 실패가 아니라 리로드 중이라는 뜻이다.

## 13. 출처

전부 Unity 공식(primary):

- [Best practices for profiling game performance](https://unity.com/how-to/best-practices-for-profiling-game-performance) — 프레임 예산, CPU/GPU-bound 판정표, GC.Alloc 인위적 duration, 모바일 35% 헤드룸
- [Profiler markers reference](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-markers.html) — 마커별 정의, Editor VSync 흉내, 렌더 스레드 분기
- [Highlights Profiler module](https://docs.unity3d.com/6000.3/Documentation/Manual/ProfilerHighlights.html) — CPU Active Time에서 빼는 대기 마커 목록
- [Play mode / Edit mode samples](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-play-edit-samples.html) — `EditorLoop`, `EditorOnly` 접두
- [Profiler counters reference](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-counters-reference.html) — 카운터 정의와 릴리즈 빌드 가용성
- [Incremental garbage collection](https://docs.unity3d.com/6000.3/Documentation/Manual/performance-incremental-garbage-collection.html)
- [Physics performance issues](https://docs.unity3d.com/Manual/physics-performance-issues.html) — 호출 수 10 임계
- [Choose a method for optimizing draw calls](https://docs.unity3d.com/6000.0/Documentation/Manual/optimizing-draw-calls-choose-method.html) — 파이프라인별 배칭 권고표
- [Camera.main](https://docs.unity3d.com/ScriptReference/Camera-main.html) — 캐시됨
- [Performance Testing package](https://docs.unity3d.com/Packages/com.unity.test-framework.performance@3.1/manual/writing-tests.html) — 1ms/5% 가드레일, warmup 기본값
