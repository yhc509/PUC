#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    public static class ExecuteValueSerializer
    {
        private const int MaxDepth = 32;
        private const int MaxNodeCount = 1_000_000;

        public static string Serialize(object? value)
        {
            return Serialize(value, CancellationToken.None);
        }

        public static string Serialize(object? value, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            var context = new SerializationContext(cancellationToken);
            Write(sb, value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), context);
            return sb.ToString();
        }

        private static void Write(
            StringBuilder sb,
            object? value,
            int depth,
            HashSet<object> seen,
            SerializationContext context)
        {
            context.EnterNode();

            switch (value)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string s: WriteString(sb, s); return;
                case char c: WriteString(sb, c.ToString()); return;
                case float f: sb.Append(F(f)); return;
                case double d: sb.Append(F(d)); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
                case Enum e: WriteString(sb, e.ToString()); return;
            }

            if (depth >= MaxDepth)
            {
                WriteString(sb, value.ToString() ?? string.Empty);
                return;
            }

            switch (value)
            {
                case Vector2 v2: sb.Append($"{{\"x\":{F(v2.x)},\"y\":{F(v2.y)}}}"); return;
                case Vector3 v3: sb.Append($"{{\"x\":{F(v3.x)},\"y\":{F(v3.y)},\"z\":{F(v3.z)}}}"); return;
                case Vector4 v4: sb.Append($"{{\"x\":{F(v4.x)},\"y\":{F(v4.y)},\"z\":{F(v4.z)},\"w\":{F(v4.w)}}}"); return;
                case Quaternion q: sb.Append($"{{\"x\":{F(q.x)},\"y\":{F(q.y)},\"z\":{F(q.z)},\"w\":{F(q.w)}}}"); return;
                case Color c: sb.Append($"{{\"r\":{F(c.r)},\"g\":{F(c.g)},\"b\":{F(c.b)},\"a\":{F(c.a)}}}"); return;
                case UnityEngine.Object unityObject:
                    sb.Append("{\"instanceID\":").Append(unityObject.GetInstanceID())
                        .Append(",\"name\":");
                    WriteString(sb, unityObject ? unityObject.name : "<null>");
                    sb.Append(",\"type\":");
                    WriteString(sb, value.GetType().Name);
                    sb.Append('}');
                    return;
            }

            if (!seen.Add(value))
            {
                sb.Append("null");
                return;
            }

            try
            {
                switch (value)
                {
                    case IDictionary dict: WriteDictionary(sb, dict, depth, seen, context); return;
                    case IEnumerable enumerable: WriteArray(sb, enumerable, depth, seen, context); return;
                    default: WriteObject(sb, value, depth, seen, context); return;
                }
            }
            finally
            {
                seen.Remove(value);
            }
        }

        private static string F(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "null";
            }

            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static string F(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "null";
            }

            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static void WriteArray(
            StringBuilder sb,
            IEnumerable enumerable,
            int depth,
            HashSet<object> seen,
            SerializationContext context)
        {
            sb.Append('[');
            bool first = true;
            foreach (object? item in enumerable)
            {
                context.CheckCancellation();

                if (!first)
                {
                    sb.Append(',');
                }

                Write(sb, item, depth + 1, seen, context);
                first = false;
            }
            sb.Append(']');
        }

        private static void WriteDictionary(
            StringBuilder sb,
            IDictionary dict,
            int depth,
            HashSet<object> seen,
            SerializationContext context)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                context.CheckCancellation();

                if (!first)
                {
                    sb.Append(',');
                }

                WriteString(sb, entry.Key?.ToString() ?? "null");
                sb.Append(':');
                Write(sb, entry.Value, depth + 1, seen, context);
                first = false;
            }
            sb.Append('}');
        }

        private static void WriteObject(
            StringBuilder sb,
            object value,
            int depth,
            HashSet<object> seen,
            SerializationContext context)
        {
            sb.Append('{');
            bool first = true;
            Type type = value.GetType();

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                context.CheckCancellation();
                first = WriteMember(sb, field.Name, SafeGet(() => field.GetValue(value)), depth, seen, context, first);
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                context.CheckCancellation();

                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                first = WriteMember(sb, property.Name, SafeGet(() => property.GetValue(value)), depth, seen, context, first);
            }

            sb.Append('}');
        }

        private static bool WriteMember(
            StringBuilder sb,
            string name,
            object? value,
            int depth,
            HashSet<object> seen,
            SerializationContext context,
            bool first)
        {
            if (!first)
            {
                sb.Append(',');
            }

            WriteString(sb, name);
            sb.Append(':');
            Write(sb, value, depth + 1, seen, context);
            return false;
        }

        private static object? SafeGet(Func<object?> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class SerializationContext
        {
            private readonly CancellationToken _cancellationToken;
            private int _nodeCount;

            public SerializationContext(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
            }

            public void CheckCancellation()
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            public void EnterNode()
            {
                CheckCancellation();
                _nodeCount++;
                if (_nodeCount > MaxNodeCount)
                {
                    throw new OperationCanceledException(
                        $"Serialized result exceeded the maximum node count of {MaxNodeCount.ToString(CultureInfo.InvariantCulture)}.");
                }
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            bool IEqualityComparer<object>.Equals(object? x, object? y)
            {
                return ReferenceEquals(x, y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
