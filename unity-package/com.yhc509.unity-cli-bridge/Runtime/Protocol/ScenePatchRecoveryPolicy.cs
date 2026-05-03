namespace UnityCli.Protocol
{
    internal static class ScenePatchRecoveryPolicy
    {
        internal static bool ShouldReloadAfterFailedPatch(bool targetWasLoaded)
        {
            return targetWasLoaded;
        }
    }
}
