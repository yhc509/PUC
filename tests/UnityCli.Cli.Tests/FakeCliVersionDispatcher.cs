using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

/// <summary>
/// Stands in for the real dispatcher, which replaces the current process and would take the test
/// runner with it.
/// </summary>
internal sealed class FakeCliVersionDispatcher : ICliVersionDispatcher
{
    private readonly IReadOnlyList<InstalledCliVersion> _installedVersions;
    private readonly int _exitCode;
    private readonly string? _execFailureMessage;

    public FakeCliVersionDispatcher(
        IReadOnlyList<InstalledCliVersion>? installedVersions = null,
        bool isDispatchGuardSet = false,
        int exitCode = 0,
        string? execFailureMessage = null)
    {
        _installedVersions = installedVersions ?? Array.Empty<InstalledCliVersion>();
        _exitCode = exitCode;
        _execFailureMessage = execFailureMessage;
        IsDispatchGuardSet = isDispatchGuardSet;
    }

    public bool IsDispatchGuardSet { get; }

    public int ListInstalledCallCount { get; private set; }

    public int ExecCallCount { get; private set; }

    public string? ExecutedExecutablePath { get; private set; }

    public string[]? ExecutedArgs { get; private set; }

    public IReadOnlyList<InstalledCliVersion> ListInstalledVersions()
    {
        ListInstalledCallCount++;
        return _installedVersions;
    }

    public int Exec(string executablePath, string[] args)
    {
        ExecCallCount++;
        ExecutedExecutablePath = executablePath;
        ExecutedArgs = args;

        if (_execFailureMessage is not null)
        {
            throw new CliDispatchException(_execFailureMessage);
        }

        return _exitCode;
    }
}
