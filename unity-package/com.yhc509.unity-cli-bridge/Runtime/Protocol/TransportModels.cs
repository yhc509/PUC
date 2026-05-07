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
        public string? protocolVersion;
        public string command = string.Empty;
        public string argumentsJson = "{}";
    }

    [Serializable]
    public sealed class ResponseEnvelope
    {
        public string requestId = string.Empty;
        public string? protocolVersion;
        public string? target;
        public string status = ProtocolConstants.StatusSuccess;
        public long durationMs;
#if UNITY_5_3_OR_NEWER
        public string? data;
#else
        public JsonElement? data;
#endif
        public ProtocolError? error;
        public bool retryable;
        public string transport = ProtocolConstants.TransportLive;

#if UNITY_5_3_OR_NEWER
        public static ResponseEnvelope Success(
            string requestId,
            string? target,
            string? data,
            long durationMs,
            string transport = ProtocolConstants.TransportLive)
        {
            return new ResponseEnvelope
            {
                requestId = requestId,
                protocolVersion = ProtocolConstants.ProtocolVersion,
                target = target,
                status = ProtocolConstants.StatusSuccess,
                durationMs = durationMs,
                data = data,
                transport = transport,
            };
        }
#else
        public static ResponseEnvelope Success(
            string requestId,
            string? target,
            JsonElement? data,
            long durationMs,
            string transport = ProtocolConstants.TransportLive)
        {
            return new ResponseEnvelope
            {
                requestId = requestId,
                protocolVersion = ProtocolConstants.ProtocolVersion,
                target = target,
                status = ProtocolConstants.StatusSuccess,
                durationMs = durationMs,
                data = data,
                transport = transport,
            };
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
                protocolVersion = ProtocolConstants.ProtocolVersion,
                target = target,
                status = ProtocolConstants.StatusError,
                durationMs = durationMs,
                retryable = retryable,
                transport = transport,
                error = new ProtocolError
                {
                    code = code,
                    message = message,
#if UNITY_5_3_OR_NEWER
                    details = details,
#else
                    details = ParseDetails(details),
#endif
                },
            };
        }

#if !UNITY_5_3_OR_NEWER
        private static JsonElement? ParseDetails(string? details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<JsonElement>(details, ProtocolJson.Default);
            }
            catch (JsonException)
            {
                return JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(details),
                    ProtocolJson.Default);
            }
        }
#endif
    }

    [Serializable]
    public sealed class ProtocolError
    {
        public string code = string.Empty;
        public string message = string.Empty;
#if UNITY_5_3_OR_NEWER
        public string? details;
#else
        public JsonElement? details;
#endif
    }
}
