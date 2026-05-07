#if UNITY_5_3_OR_NEWER
using System;
namespace UnityCli.Protocol
{
    public static class ProtocolJson
    {
        public static string Serialize<T>(T value)
        {
            return UnityEngine.JsonUtility.ToJson(value, false);
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }

            return UnityEngine.JsonUtility.FromJson<T>(json);
        }
    }
}
#else
using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityCli.Protocol
{
    public static class ProtocolJson
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
            MaxDepth = 128,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Default);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Default)!;
        }
    }
}
#endif
