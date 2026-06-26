# Prefab Workflows

## 기본 원칙

- 간단한 빈 prefab 생성은 `asset create --type prefab`보다 `prefab create`를 우선한다.
- 구조 수정 전에는 `prefab inspect --with-values`를 먼저 실행한다.
- patch spec의 node path는 `/`, `/Visual[0]`, `/Visual[0]/MeshRoot[0]` 형식이다.
- component field 값은 `SerializedProperty.propertyPath` 기준으로 적는다.

## 생성 흐름

```bash
ucli prefab create --project "$PROJECT" --path Assets/Prefabs/Enemy.prefab --spec-file /tmp/enemy-prefab.json --force --output compact
```

빠르게 시작할 때는 설치된 스킬의 `assets/prefab-create-basic.json`을 복사해서 수정한다.

> **spec에는 `root` 래핑이 필수다.** `{"root": {"name": "Enemy", "children": [...]}}` 형식으로 작성한다. `root` 없이 바로 `{"name": "Enemy"}`를 넣으면 파싱 실패한다.

## 조회 흐름

```bash
# 구조 확인 (깊이 제한 + 기본값 생략)
ucli prefab inspect --project "$PROJECT" --path Assets/Prefabs/Enemy.prefab --max-depth 2 --omit-defaults --output compact

# component 값까지 확인 (patch 전 필수)
ucli prefab inspect --project "$PROJECT" --path Assets/Prefabs/Enemy.prefab --with-values --output compact
```

inspect 결과에서 꼭 보는 값:

- `root.children[].path`
- `components[].type`
- `components[].values`

`--omit-defaults` 결과는 read-only이다. patch input으로 그대로 쓰면 생략된 필드가 복원되지 않는다.

## 수정 흐름

```bash
ucli prefab patch --project "$PROJECT" --path Assets/Prefabs/Enemy.prefab --spec-file /tmp/enemy-patch.json --output compact
```

지원 op:

- `add-child`
- `remove-node`
- `set-node`
- `add-component`
- `remove-component`
- `set-component-values`

빠르게 시작할 때는 설치된 스킬의 `assets/prefab-patch-boxcollider.json`을 기준으로 잡는다.

### set-node vs set-component-values

이 둘은 같은 컴포넌트(예: Transform)를 수정하지만 경로 체계가 다르다:

| op | 경로 체계 | 예시 | 용도 |
|---|---|---|---|
| `set-node` | 고수준 spec 키 | `transform.localPosition`, `transform.localScale` | 빈번한 Transform 수정에 간편 |
| `set-component-values` | `SerializedProperty.propertyPath` | `m_LocalPosition.x`, `m_LocalScale.y` | 모든 컴포넌트의 모든 필드에 접근 가능 |

- `set-node`는 Transform, name 등 미리 정의된 고수준 키만 지원한다. **인식 안 되는 키는 경고가 반환된다.**
- `set-component-values`는 `SerializedProperty.propertyPath`를 그대로 사용하므로 inspect 결과의 `values` 키를 기준으로 쓴다.
- 같은 노드에 두 op을 동시에 쓸 수 있다. 먼저 `set-node`로 Transform을 잡고, `set-component-values`로 세부 필드를 조정하는 패턴.

## 자주 쓰는 값 형식

Vector3:

```json
{ "x": 0, "y": 1, "z": 0 }
```

asset reference:

```json
{ "assetPath": "Assets/Physics/EnemyHit.physicMaterial" }
```

또는

```json
{ "guid": "..." }
```

## 제한사항

- root prefab 이름은 저장 후 파일 이름으로 정규화된다.
- prefab 내부 object reference와 scene object reference는 아직 일반화되어 있지 않다.
- field 이름은 추측하지 말고 inspect 결과를 기준으로 쓴다.
