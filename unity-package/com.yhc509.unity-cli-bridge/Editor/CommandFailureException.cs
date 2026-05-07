using System;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed class CommandFailureException : Exception
    {
        public CommandFailureException(string errorCode, string message, string details = null, bool isRetryable = false)
            : base(message)
        {
            ErrorCode = errorCode;
            Details = ProtocolErrorDetails.FromString(details);
            IsRetryable = isRetryable;
        }

        public CommandFailureException(string errorCode, string message, bool isRetryable, string details = null)
            : this(errorCode, message, details, isRetryable)
        {
        }

        public string ErrorCode { get; private set; }
        public string Details { get; private set; }
        public bool IsRetryable { get; private set; }
    }
}
