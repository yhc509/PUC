#nullable enable
using System;
#if !UNITY_5_3_OR_NEWER
using System.Text.Json;
#endif

namespace UnityCli.Protocol
{
    [Serializable]
    public sealed class CommandEnvelope
    {
        public string requestId = string.Empty;
        public string command = string.Empty;
        public string argumentsJson = "{}";
    }

    [Serializable]
    public sealed class ResponseEnvelope
    {
        public string requestId = string.Empty;
        public string? target;
        public string status = ProtocolConstants.StatusSuccess;
        public long durationMs;
        // INVARIANT: In the CLI build, data is always a JsonElement after EnsureData().
        // When Bridge eventually sends data directly (post-JsonUtility migration),
        // --json mode serialization may need custom handling for non-JsonElement types.
        [System.NonSerialized]
        public object? data;
        public string? dataJson;
        public ProtocolError? error;
        public bool retryable;
        public string transport = ProtocolConstants.TransportLive;

        public static ResponseEnvelope Success(
            string requestId,
            string? target,
            string? dataJson,
            long durationMs,
            string transport = ProtocolConstants.TransportLive,
            object? data = null)
        {
            return new ResponseEnvelope
            {
                requestId = requestId,
                target = target,
                status = ProtocolConstants.StatusSuccess,
                durationMs = durationMs,
                data = data,
                dataJson = dataJson,
                transport = transport,
            };
        }

        public void EnsureData()
        {
            if (data is not null)
            {
                return;
            }

            data = DeserializeData(dataJson);
        }

#if UNITY_5_3_OR_NEWER
        private static object? DeserializeData(string? dataJson)
        {
            // Unity's JsonUtility cannot serialize object-typed fields, so the bridge
            // continues to rely on dataJson until the transport fully moves to v2.
            return null;
        }
#else
        private static object? DeserializeData(string? dataJson)
        {
            if (string.IsNullOrWhiteSpace(dataJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<JsonElement>(dataJson, ProtocolJson.Default);
        }
#endif

        public static ResponseEnvelope Failure(
            string requestId,
            string? target,
            string code,
            string message,
            bool retryable,
            long durationMs = 0,
            string transport = ProtocolConstants.TransportLive,
            string? details = null)
        {
            return new ResponseEnvelope
            {
                requestId = requestId,
                target = target,
                status = ProtocolConstants.StatusError,
                durationMs = durationMs,
                retryable = retryable,
                transport = transport,
                error = new ProtocolError
                {
                    code = code,
                    message = message,
                    details = details,
                },
            };
        }
    }

    [Serializable]
    public sealed class ProtocolError
    {
        public string code = string.Empty;
        public string message = string.Empty;
        public string? details;
    }
}
