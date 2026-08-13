namespace UnityCli.Protocol
{
    /// <summary>
    /// Fixed-time equality for the IPC auth token.
    ///
    /// The transport is an owner-only local socket / named pipe, so the practical risk from a
    /// timing side channel is low — but the bridge's token check is the only authentication
    /// gate it has, and a length-prefixed early-exit comparison is observable to any other
    /// local process. Hand-rolled instead of CryptographicOperations.FixedTimeEquals so the
    /// same source compiles on Unity's runtime profile and on .NET 9.
    /// </summary>
    internal static class AuthTokenComparison
    {
        /// <summary>
        /// Compare two tokens without an early exit. Length differences are folded into the
        /// same accumulator instead of short-circuiting; an empty or missing token is always
        /// a mismatch.
        /// </summary>
        internal static bool FixedTimeEquals(string expected, string candidate)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            int difference = expected.Length ^ candidate.Length;
            int comparedLength = expected.Length < candidate.Length ? expected.Length : candidate.Length;
            for (int index = 0; index < comparedLength; index++)
            {
                difference |= expected[index] ^ candidate[index];
            }

            return difference == 0;
        }
    }
}
