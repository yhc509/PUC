#nullable enable
using System;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    /// <summary>
    /// Component with one serialized field per shape <see cref="SerializedValueApplier"/> has to
    /// translate. Kept as a real MonoBehaviour (rather than borrowing a built-in component) so the
    /// tests can cover every branch without depending on engine field names.
    /// </summary>
    public sealed class SerializedValueProbe : MonoBehaviour
    {
        public enum ProbeMode
        {
            Idle,
            Running,
            Stopped,
        }

        public Vector2 vector2Value;
        public Vector3 vector3Value;
        public Vector4 vector4Value;
        public Vector3Int vector3IntValue;
        public Quaternion quaternionValue;
        public Rect rectValue;
        public Color colorValue = Color.black;
        public Bounds boundsValue;
        public string stringValue = string.Empty;
        public float floatValue;
        public int intValue;
        public ProbeMode enumValue;
        public int[] intArrayValue = Array.Empty<int>();
    }
}
