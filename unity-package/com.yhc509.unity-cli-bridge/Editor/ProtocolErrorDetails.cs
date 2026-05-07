#nullable enable
using Newtonsoft.Json;

namespace UnityCliBridge.Bridge.Editor
{
    internal static class ProtocolErrorDetails
    {
        public static string? FromString(string? value)
        {
            return value == null ? null : JsonConvert.ToString(value);
        }
    }
}
