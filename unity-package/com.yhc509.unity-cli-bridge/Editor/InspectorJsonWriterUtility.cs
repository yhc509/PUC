#nullable enable
using Newtonsoft.Json;
using UnityCli.Protocol;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal static class InspectorJsonWriterUtility
    {
        internal static JsonTextWriter Configure(JsonTextWriter writer)
        {
            writer.FloatFormatHandling = FloatFormatHandling.String;
            return writer;
        }

        internal static void WriteAssetToken(JsonWriter writer, string path)
        {
            AssetRecord record = AssetCommandSupport.BuildRecordFromPath(path, allowPackages: true);
            writer.WriteStartObject();
            writer.WritePropertyName("path");
            writer.WriteValue(record.path);
            writer.WritePropertyName("guid");
            writer.WriteValue(record.guid);
            writer.WritePropertyName("assetName");
            writer.WriteValue(record.assetName);
            writer.WritePropertyName("mainType");
            writer.WriteValue(record.mainType);
            writer.WritePropertyName("isFolder");
            writer.WriteValue(record.isFolder);
            writer.WritePropertyName("exists");
            writer.WriteValue(record.exists);
            writer.WriteEndObject();
        }

        internal static void WriteLayerToken(JsonWriter writer, int layerIndex)
        {
            string layerName = LayerMask.LayerToName(layerIndex);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                writer.WriteValue(layerIndex);
                return;
            }

            writer.WriteValue(layerName);
        }

        internal static bool ShouldWriteTransformToken(Transform transform, bool omitDefaults)
        {
            return !omitDefaults
                || !IsExactlyZero(transform.localPosition)
                || !IsExactlyIdentity(transform.localRotation)
                || !IsExactlyOne(transform.localScale);
        }

        internal static void WriteTransformToken(JsonWriter writer, Transform transform, bool omitDefaults)
        {
            writer.WriteStartObject();
            if (!omitDefaults || !IsExactlyZero(transform.localPosition))
            {
                writer.WritePropertyName("localPosition");
                WriteVector3Token(writer, transform.localPosition);
            }

            if (!omitDefaults || !IsExactlyIdentity(transform.localRotation))
            {
                writer.WritePropertyName("localRotationEuler");
                WriteVector3Token(writer, transform.localEulerAngles);
            }

            if (!omitDefaults || !IsExactlyOne(transform.localScale))
            {
                writer.WritePropertyName("localScale");
                WriteVector3Token(writer, transform.localScale);
            }

            writer.WriteEndObject();
        }

        internal static void WriteVector3Token(JsonWriter writer, Vector3 vector)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(vector.x);
            writer.WritePropertyName("y");
            writer.WriteValue(vector.y);
            writer.WritePropertyName("z");
            writer.WriteValue(vector.z);
            writer.WriteEndObject();
        }

        private static bool IsExactlyZero(Vector3 value)
        {
            return value.x == 0f && value.y == 0f && value.z == 0f;
        }

        private static bool IsExactlyOne(Vector3 value)
        {
            return value.x == 1f && value.y == 1f && value.z == 1f;
        }

        private static bool IsExactlyIdentity(Quaternion value)
        {
            return value.x == 0f && value.y == 0f && value.z == 0f && value.w == 1f;
        }
    }
}
