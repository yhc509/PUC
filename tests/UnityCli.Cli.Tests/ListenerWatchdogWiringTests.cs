using UnityCli.DocGen;

namespace UnityCli.Cli.Tests;

/// <summary>
/// Source-level guards for the listener watchdog wiring in <c>BridgeHost</c>. The policy itself is
/// covered by <see cref="ListenerWatchdogPolicyTests"/>; what cannot be unit-tested is that the
/// host actually reports listener death and stops advertising when recovery fails.
/// </summary>
public sealed class ListenerWatchdogWiringTests
{
    private static string ReadBridgeHost()
    {
        string repoRoot = RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            "BridgeHost.cs"));
    }

    [Fact]
    public void BridgeHost_RunsTheWatchdogFromTheEditorUpdateHook()
    {
        string source = ReadBridgeHost();

        Assert.Contains("RunListenerWatchdog();", source);
        Assert.Contains("new ListenerWatchdogPolicy(", source);
        Assert.Contains("EditorApplication.isCompiling || EditorApplication.isUpdating", source);
    }

    [Fact]
    public void BridgeHost_ClearsListenerReadinessWhenAnAcceptLoopEnds()
    {
        string source = ReadBridgeHost();

        // Both accept loops must report death, otherwise the heartbeat keeps advertising an
        // instance the CLI can resolve but never connect to.
        Assert.True(
            CountOccurrences(source, "_isListenerReady = false;") >= 2,
            "Each accept loop should clear listener readiness in its finally block.");
    }

    [Fact]
    public void BridgeHost_NeverRacesAnInFlightBind()
    {
        string source = ReadBridgeHost();

        Assert.Contains("_isListenerStarting = true;", source);
        Assert.True(
            CountOccurrences(source, "_isListenerStarting = false;") >= 4,
            "Every bind outcome — success, cancellation, failure — must clear the starting flag.");
    }

    [Fact]
    public void BridgeHost_StopsAdvertisingWhenRecoveryIsAbandoned()
    {
        string source = ReadBridgeHost();

        Assert.Contains("case ListenerWatchdogDecision.Abandon:", source);
        Assert.Contains("private void AbandonListener()", source);

        int abandonIndex = source.IndexOf("private void AbandonListener()", StringComparison.Ordinal);
        string abandonBody = source.Substring(abandonIndex);
        Assert.Contains("UnregisterInstance();", abandonBody);

        // The heartbeat branch has to honour the abandoned state.
        Assert.Contains("if (_isListenerAbandoned)", source);
    }

    [Fact]
    public void BridgeHost_RepublishesRegistryStateAfterARebind()
    {
        string source = ReadBridgeHost();

        int recoveredIndex = source.IndexOf("private void OnListenerRecovered()", StringComparison.Ordinal);
        Assert.True(recoveredIndex >= 0, "The watchdog needs a recovery handler.");

        string recoveredBody = source.Substring(recoveredIndex);
        Assert.Contains("WriteTokenSidecarSafely();", recoveredBody);
        Assert.Contains("RegisterInstance();", recoveredBody);

        // A rebind can land on a different hash; the old sidecar would otherwise keep a live token.
        Assert.Contains("DeleteTokenSidecar(_registryFilePath, _projectHashBeforeRestart)", recoveredBody);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = source.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
