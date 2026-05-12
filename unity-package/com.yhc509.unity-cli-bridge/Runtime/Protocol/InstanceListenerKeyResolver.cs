#nullable enable
using System;
using System.Globalization;

namespace UnityCli.Protocol
{
    public static class InstanceListenerKeyResolver
    {
        public static T Acquire<T>(
            string baseHash,
            int maxAttempts,
            Func<string, T?> tryBindWithHash,
            out string acquiredHash)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(baseHash))
            {
                throw new ArgumentException("baseHash must not be empty.", nameof(baseHash));
            }

            if (maxAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be at least 1.");
            }

            if (tryBindWithHash == null)
            {
                throw new ArgumentNullException(nameof(tryBindWithHash));
            }

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate = attempt == 0
                    ? baseHash
                    : baseHash + "-" + attempt.ToString(CultureInfo.InvariantCulture);
                T? listener = tryBindWithHash(candidate);
                if (listener != null)
                {
                    acquiredHash = candidate;
                    return listener;
                }
            }

            throw new InvalidOperationException(
                "Failed to acquire listener key for base hash '"
                + baseHash
                + "' after "
                + maxAttempts.ToString(CultureInfo.InvariantCulture)
                + " attempts.");
        }
    }
}
