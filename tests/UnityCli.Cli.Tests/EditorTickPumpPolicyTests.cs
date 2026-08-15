using UnityCli.DocGen;

namespace UnityCli.Cli.Tests;

/// <summary>
/// The tick pump only speeds up work that is subscribed through it. These are source-level guards
/// for the wiring — the pump itself needs a live editor, so it cannot be exercised here.
/// </summary>
public sealed class EditorTickPumpPolicyTests
{
    public static TheoryData<string> DeferredHandlerFiles()
    {
        var files = new TheoryData<string>();
        files.Add("TestCommandHandler.cs");
        files.Add("TestCommandHandler.PlayMode.cs");
        files.Add("PackageCommandHandler.cs");
        files.Add("ProfileCommandHandler.cs");
        files.Add("ProfileCommandHandler.Capture.cs");
        files.Add("ProfileCommandHandler.Memory.cs");
        files.Add("QaCommandHandler.cs");
        files.Add("QaCommandHandler.Sequence.cs");
        files.Add("RecordCommandHandler.cs");
        return files;
    }

    private static string ReadEditorSource(string fileName)
    {
        string repoRoot = RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            fileName));
    }

    [Theory]
    [MemberData(nameof(DeferredHandlerFiles))]
    public void DeferredHandlers_SubscribeThroughTheTickPump(string fileName)
    {
        string source = ReadEditorSource(fileName);

        // A direct subscription still works, it just silently opts that flow out of the pump and
        // leaves it running at the unfocused editor's throttled tick rate.
        Assert.DoesNotContain("EditorApplication.update +=", source);
        Assert.DoesNotContain("EditorApplication.update -=", source);
        Assert.Contains("EditorTickPump.", source);
    }

    [Theory]
    [MemberData(nameof(DeferredHandlerFiles))]
    public void DeferredHandlers_ReleaseThePumpOnEveryPathThatSubscribes(string fileName)
    {
        string source = ReadEditorSource(fileName);
        int adds = CountOccurrences(source, "EditorTickPump.Add(");
        int removes = CountOccurrences(source, "EditorTickPump.Remove(");

        // Removes are idempotent and the poll bodies exit on several paths, so the useful
        // invariant is that nothing subscribes without a matching teardown somewhere.
        Assert.True(adds > 0, fileName + " should drive at least one deferred poll.");
        Assert.True(
            removes >= adds,
            fileName + " subscribes " + adds + " poll(s) but only unsubscribes " + removes + " time(s).");
    }

    [Fact]
    public void TickPump_BindsSignalTickReflectivelyAndDegradesWhenMissing()
    {
        string source = ReadEditorSource("EditorTickPump.cs");

        Assert.Contains("\"SignalTick\"", source);
        Assert.Contains("BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public", source);
        Assert.Contains("Delegate.CreateDelegate(typeof(Action), method)", source);

        // An internal API that disappears must cost speed, not availability.
        Assert.DoesNotContain("throw new", source);
    }

    [Fact]
    public void TickPump_RunsOnlyWhileDeferredWorkIsSubscribed()
    {
        string source = ReadEditorSource("EditorTickPump.cs");

        Assert.Contains("_subscribers.Count > 0", source);
        Assert.Contains("EditorApplication.update -= Pump;", source);
    }

    [Fact]
    public void BridgeHost_KeepsItsOwnUpdateHookOutOfThePump()
    {
        string source = ReadEditorSource("BridgeHost.cs");

        // The host update hook is permanent; routing it through the pump would pin the editor at
        // full tick rate for the entire editor session.
        Assert.Contains("EditorApplication.update += OnEditorUpdate;", source);
        Assert.DoesNotContain("EditorTickPump.Add(OnEditorUpdate)", source);
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
