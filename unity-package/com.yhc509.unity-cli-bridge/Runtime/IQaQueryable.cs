#nullable enable

namespace UnityCliBridge.Bridge
{
    /// <summary>
    /// Implement on a MonoBehaviour to expose game-specific state values to qa run-sequence conditions.
    /// The bridge polls TryQaQuery(key) and compares the returned value with the spec's operator.
    /// Return false for an unknown key. Supported value types: float/int/double/bool/string/Vector2/Vector3.
    /// </summary>
    public interface IQaQueryable
    {
        bool TryQaQuery(string key, out object? value);
    }
}
