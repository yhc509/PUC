using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

// InstallRootScope and BuildNoMatchingVersionResponse both read the process-wide install-root
// environment variable, so these must not run alongside the tests that mutate it.
[Collection(CurrentDirectoryCollection.Name)]
public sealed class CliDispatchPolicyTests
{
    [Theory]
    [InlineData(CommandKind.InstancesList)]
    [InlineData(CommandKind.InstancesUse)]
    [InlineData(CommandKind.Doctor)]
    [InlineData(CommandKind.QaWait)]
    [InlineData(CommandKind.Help)]
    public void IsLocalOnlyCommand_CoversCommandsThatNeverNeedLiveIpc(CommandKind kind)
    {
        Assert.True(CliDispatchPolicy.IsLocalOnlyCommand(kind));
    }

    [Theory]
    [InlineData(CommandKind.Compile)]
    [InlineData(CommandKind.ExecuteCode)]
    [InlineData(CommandKind.ScenePatch)]
    [InlineData(CommandKind.TestRun)]
    // status returns the bridge's envelope verbatim, so a mismatch fails the command. It must hand
    // off like any other live command.
    [InlineData(CommandKind.Status)]
    public void IsLocalOnlyCommand_IsFalseForLiveCommands(CommandKind kind)
    {
        Assert.False(CliDispatchPolicy.IsLocalOnlyCommand(kind));
    }

    [Fact]
    public void Decide_Status_DispatchesOnMismatch()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions: [Version("0.3.5", "4")]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Status, Mismatch("4"), dispatcher);

        Assert.Equal(DispatchAction.Exec, decision.Action);
        Assert.Equal("0.3.5", decision.Match!.Version);
    }

    [Fact]
    public void Decide_WhenResponseIsNotProtocolMismatch_DoesNothing()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions: [Version("0.3.5", "4")]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Compile, Success(), dispatcher);

        Assert.Equal(DispatchAction.None, decision.Action);
        Assert.Equal(0, dispatcher.ListInstalledCallCount);
    }

    [Fact]
    public void Decide_WhenCommandIsLocalOnly_DoesNotDispatchEvenOnMismatch()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions: [Version("0.3.5", "4")]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Doctor, Mismatch("4"), dispatcher);

        Assert.Equal(DispatchAction.None, decision.Action);
        Assert.Equal(0, dispatcher.ListInstalledCallCount);
    }

    [Fact]
    public void Decide_WhenDispatchGuardIsSet_DoesNotDispatchAgain()
    {
        var dispatcher = new FakeCliVersionDispatcher(
            installedVersions: [Version("0.3.5", "4")],
            isDispatchGuardSet: true);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Compile, Mismatch("4"), dispatcher);

        Assert.Equal(DispatchAction.None, decision.Action);
        Assert.Equal(0, dispatcher.ListInstalledCallCount);
    }

    [Fact]
    public void Decide_WhenBridgeDidNotReportItsProtocol_DoesNothing()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions: [Version("0.3.5", "4")]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Compile, Mismatch(string.Empty), dispatcher);

        Assert.Equal(DispatchAction.None, decision.Action);
    }

    [Fact]
    public void Decide_WhenBridgeReportsOurOwnProtocol_DoesNotDispatchToItself()
    {
        var dispatcher = new FakeCliVersionDispatcher(
            installedVersions: [Version("0.4.1", ProtocolConstants.ProtocolVersion)]);

        DispatchDecision decision = CliDispatchPolicy.Decide(
            CommandKind.Compile,
            Mismatch(ProtocolConstants.ProtocolVersion),
            dispatcher);

        Assert.Equal(DispatchAction.None, decision.Action);
    }

    [Fact]
    public void Decide_WhenAnInstalledVersionSpeaksTheBridgeProtocol_Execs()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions:
        [
            Version("0.4.1", "5"),
            Version("0.3.5", "4"),
        ]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Compile, Mismatch("4"), dispatcher);

        Assert.Equal(DispatchAction.Exec, decision.Action);
        Assert.Equal("0.3.5", decision.Match!.Version);
        Assert.Equal("4", decision.RequiredProtocolVersion);
    }

    [Fact]
    public void Decide_WhenSeveralVersionsShareTheProtocol_PicksNewest()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions:
        [
            Version("0.3.4", "4"),
            Version("0.3.5", "4"),
            Version("0.4.1", "5"),
        ]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Compile, Mismatch("4"), dispatcher);

        Assert.Equal(DispatchAction.Exec, decision.Action);
        Assert.Equal("0.3.5", decision.Match!.Version);
    }

    [Fact]
    public void Decide_WhenNoInstalledVersionSpeaksTheBridgeProtocol_ReportsMissingVersion()
    {
        var dispatcher = new FakeCliVersionDispatcher(installedVersions: [Version("0.4.1", "5")]);

        DispatchDecision decision = CliDispatchPolicy.Decide(CommandKind.Compile, Mismatch("4"), dispatcher);

        Assert.Equal(DispatchAction.NoMatchingVersion, decision.Action);
        Assert.Equal("4", decision.RequiredProtocolVersion);
        Assert.Single(decision.InstalledVersions);
    }

    [Fact]
    public void BuildNoMatchingVersionResponse_KeepsProtocolMismatchCodeAndIsActionable()
    {
        var installed = new[] { Version("0.4.1", "5") };

        ResponseEnvelope response = CliDispatchPolicy.BuildNoMatchingVersionResponse(Mismatch("4"), "4", installed);

        Assert.Equal(ProtocolConstants.StatusError, response.status);
        Assert.Equal(ProtocolConstants.ErrorProtocolMismatch, response.error?.code);
        Assert.False(response.retryable);
        Assert.Contains("protocol 4", response.error!.message);
        Assert.Contains("Window > Unity CLI Manager > Install CLI", response.error.message);
        Assert.Contains("v0.4.1", response.error.details?.GetString());

        // The envelope must name the protocol the bridge needs, not ours, or it contradicts itself.
        Assert.Equal("4", response.protocolVersion);
    }

    [Fact]
    public void BuildHandoffFailedResponse_KeepsTheMismatchAndRecordsWhyRoutingFailed()
    {
        InstalledCliVersion match = Version("0.3.5", "4");

        ResponseEnvelope response = CliDispatchPolicy.BuildHandoffFailedResponse(
            Mismatch("4"),
            match,
            "execve failed with errno 13.");

        Assert.Equal(ProtocolConstants.ErrorProtocolMismatch, response.error?.code);
        Assert.Equal("4", response.protocolVersion);
        Assert.Contains("incompatible", response.error!.message);

        string? details = response.error.details?.GetString();
        Assert.Contains("errno 13", details);
        Assert.Contains(match.ExecutablePath, details);
    }

    [Fact]
    public void BuildNoMatchingVersionResponse_WhenNothingIsInstalled_PointsAtTheVersionsDirectory()
    {
        ResponseEnvelope response = CliDispatchPolicy.BuildNoMatchingVersionResponse(
            Mismatch("4"),
            "4",
            Array.Empty<InstalledCliVersion>());

        Assert.Equal(ProtocolConstants.ErrorProtocolMismatch, response.error?.code);
        Assert.Contains(CliInstallLayout.GetVersionsDirectory(), response.error?.details?.GetString());
    }

    private static InstalledCliVersion Version(string version, string protocolVersion) =>
        new(version, protocolVersion, "/versions/" + version, "/versions/" + version + "/unity-cli");

    private static ResponseEnvelope Success()
    {
        return ResponseEnvelope.Success("req-1", "hash", data: null, durationMs: 1);
    }

    private static ResponseEnvelope Mismatch(string bridgeProtocolVersion)
    {
        var response = ResponseEnvelope.Failure(
            "req-1",
            "hash",
            ProtocolConstants.ErrorProtocolMismatch,
            "CLI version is incompatible with this Unity package.",
            retryable: false);
        response.protocolVersion = bridgeProtocolVersion;
        return response;
    }
}
