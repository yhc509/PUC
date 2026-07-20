# Troubleshooting

## `liveReachable: false`

- Unity가 import나 compile 중일 수 있다.
- 프로젝트 경로가 심링크 경로일 수 있다. `pwd -P`로 다시 잡는다.
- `instances list`로 다른 프로젝트에 붙은 것은 아닌지 확인한다.

## busy 상태

- `isCompiling` 또는 `isUpdating`이 true면 쓰기 명령은 재시도 흐름으로 본다.
- 이때는 `status`, `read-console`, `asset info`, `asset find`, `prefab inspect` 같은 읽기 위주 명령만 유지한다.

## stale instance

- registry는 `~/Library/Application Support/unity-cli/instances.json`에 있다.
- 오래된 인스턴스가 보이면 에디터를 모두 닫고 다시 열기 전 이 파일 상태를 확인한다.

## live 연결 문제

- 편집기 상태를 바꾸는 명령과 asset/material/package/scene/prefab 명령은 모두 live 연결이 필요하다.
- `liveReachable: false`면 에디터를 열고 bridge import/compile이 끝날 때까지 기다린 뒤 다시 시도한다.
- 프로젝트를 잘못 잡았을 수 있으니 `pwd -P`, `status --project ... --json`, `instances use`를 다시 확인한다.

## 로그 확인

- 성공 응답만 보고 닫지 않는다.
- live 작업 뒤에는 먼저 `read-console --no-stacktrace --output compact` 한 번으로 error/warning/log를 같이 본다.
- 특정 타입만 필요할 때만 `--type error`나 `--type warning`으로 좁힌다.
- 새 에러나 경고가 있으면 먼저 그 원인을 설명하고, 성공으로 보고하지 않는다.

## Test Runner 에러 코드

- `TEST_RUN_IN_PROGRESS` — single-flight 락 충돌. 진행 중인 `runId`가 동봉되므로 기다리거나 watchdog deadline 만료 뒤 다시 확인한다. 콜백 누락 등으로 락이 안 풀리면 `test cancel`로 수동 해제한다.
- `TEST_RUN_TIMEOUT` — `--timeout` 초과. result payload `status: TimedOut`이며 CLI exit code 1로 반환된다.
- `TEST_PLAYMODE_ENTRY_FAILED` — `TestRunnerApi.Execute` 뒤 15초 안에 `isPlaying = true`로 전환되지 않았다. dirty scene, 컴파일 중, asmdef 인식 실패를 먼저 본다.
- `TEST_LIST_TIMEOUT` — `RetrieveTestList` 콜백이 30초 내 도착하지 않았다. Editor가 compile 또는 asset import 중일 수 있다.
- `TEST_RUN_NOT_FOUND` — `test results --run-id <id>`의 디스크 캐시가 없다. 먼저 `last-run.json`이 가리키는 ID를 확인한다.
- `TEST_INVALID_MODE` — `--mode` 값이 허용 범위를 벗어났다. `test run`은 `edit`/`play`, `test list`는 `edit`/`play`/`all`을 쓴다.
- `TEST_RUN_FAILED` / `TEST_RUN_CANCELLED` / `TEST_RUN_INTERRUPTED` — 비-`Completed` 종료 상태. envelope error로 변환되어 CLI exit code 1이 된다.
- `CLI_USAGE` — `--mode edit --no-domain-reload` 같은 금지 조합. 메시지에 맞춰 옵션을 수정한다.
