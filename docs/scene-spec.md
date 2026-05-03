# Scene Spec

## Summary

Use `scene inspect` and `scene patch` to edit saved `Assets/...unity` scenes in a deterministic way.

## `scene open`

```bash
unity-cli scene open --path Assets/Scenes/System.unity [--force]
```

Rules:

- `--path` targets a saved `.unity` asset
- if currently loaded scenes are dirty, `--force` is required to discard them and open the target scene

## `scene inspect`

Use `--with-values` when you need data for authoring a patch.

- `/` is the virtual scene root
- Root object paths look like `/Environment[0]`, `/Player[0]`
- Child paths look like `/Environment[0]/Props[0]/Torch[1]`
- Component types are reported by full name
- Component values are reported by writable `SerializedProperty.propertyPath`

Do not guess GameObject paths or field names when writing `scene patch`. Use `scene inspect --with-values` as the source of truth.

## `scene patch`

Supported operations:

- `add-gameobject`
- `modify-gameobject`
- `delete-gameobject`
- `add-component`
- `modify-component`
- `remove-component`

Example:

```json
{
  "version": 1,
  "operations": [
    {
      "op": "add-gameobject",
      "parent": "/",
      "node": {
        "name": "SpawnPoint",
        "primitive": "Cube",
        "transform": {
          "localPosition": { "x": 0, "y": 1, "z": 0 }
        }
      }
    },
    {
      "op": "add-component",
      "target": "/SpawnPoint[0]",
      "component": {
        "type": "UnityEngine.BoxCollider"
      }
    },
    {
      "op": "modify-component",
      "target": "/SpawnPoint[0]",
      "componentType": "UnityEngine.BoxCollider",
      "values": {
        "m_IsTrigger": true
      }
    }
  ]
}
```

Rules:

- `version` is currently `1`
- `delete-gameobject` and `remove-component` require `--force`
- if the target scene is already loaded in the Editor, `scene inspect` expects it to be clean and `scene patch` refuses unsaved target-scene changes even with `--force`
- `scene patch` writes through a same-folder hidden `.bridge-bak` backup for the `.unity` file and `.meta`
- `modify-gameobject` uses `values` with `name`, `active`, `tag`, `layer`, and `transform`
- `add-gameobject` can include nested `children`, initial `components`, and an optional `primitive` of `Cube`, `Sphere`, `Capsule`, `Cylinder`, `Plane`, or `Quad`
- `modify-component` uses writable serialized field names from inspect output

## Value Rules

- Vector, Color, Rect, and similar structs are passed as JSON objects
- Asset references are written as `{ "assetPath": "Assets/..." }` or `{ "guid": "..." }`
- Component type lookup prefers the full name. A short name is only allowed when it is unique.
- The same JSON spec format is used by the live CLI and bridge command handlers

## Limits

- Scene patching currently targets saved `Assets/...unity` assets
- Multi-scene orchestration is out of scope for now
- Scene object reference generalization is not implemented yet
