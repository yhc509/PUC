using UnityCli.DocGen;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class PackageDeferredPolicyTests
{
    public static TheoryData<string> PackageCommands()
    {
        var commands = new TheoryData<string>();
        commands.Add(ProtocolConstants.CommandPackageList);
        commands.Add(ProtocolConstants.CommandPackageAdd);
        commands.Add(ProtocolConstants.CommandPackageRemove);
        commands.Add(ProtocolConstants.CommandPackageSearch);
        return commands;
    }

    [Theory]
    [MemberData(nameof(PackageCommands))]
    public void IsDeferredPackageCommand_ReturnsTrueForAllPackageCommands(string command)
    {
        Assert.True(ProtocolHelpers.IsPackageCommand(command));
        Assert.True(ProtocolHelpers.IsDeferredPackageCommand(command));
    }

    [Fact]
    public void PackageRequestTimeoutPolicy_UsesFiveMinuteDefault()
    {
        Assert.Equal(300, ProtocolConstants.DefaultPackageRequestTimeoutSeconds);
        Assert.False(ProtocolConstants.IsPackageRequestTimedOut(TimeSpan.FromMilliseconds(299_999)));
        Assert.True(ProtocolConstants.IsPackageRequestTimedOut(TimeSpan.FromSeconds(300)));
    }

    [Fact]
    public void PackageRequestTimeoutPolicy_UsesPackageTimeoutError()
    {
        Assert.Equal("PACKAGE_TIMEOUT", ProtocolConstants.ErrorPackageTimeout);
        Assert.Equal(
            "패키지 명령이 300초 안에 완료되지 않았습니다. Editor의 Package Manager 상태를 확인해 주세요.",
            ProtocolConstants.BuildPackageRequestTimeoutMessage(300));
    }

    [Fact]
    public void EditorPackageDispatch_UsesDeferredRouteWithoutSleepPolling()
    {
        string repoRoot = RepositoryPaths.FindRepoRoot(AppContext.BaseDirectory);
        string bridgeHost = File.ReadAllText(Path.Combine(
            repoRoot,
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            "BridgeHost.cs"));
        string packageHandler = File.ReadAllText(Path.Combine(
            repoRoot,
            "unity-package",
            "com.yhc509.unity-cli-bridge",
            "Editor",
            "PackageCommandHandler.cs"));

        Assert.Contains(
            "_packageCommandHandler.CanHandle(pending.Command.command) && _packageCommandHandler.IsDeferred",
            bridgeHost);
        Assert.Contains("StartDeferredPackageRequest(pending);", bridgeHost);
        Assert.Contains(
            "_packageCommandHandler.StartDeferred(command.command, command.argumentsJson, pending.Completion, _projectHash);",
            bridgeHost);

        Assert.DoesNotContain("Thread.Sleep", packageHandler);
        Assert.DoesNotContain("WaitForRequest", packageHandler);
        Assert.Contains("EditorApplication.update += Poll;", packageHandler);
        Assert.Contains("ProtocolConstants.ErrorPackageTimeout", packageHandler);
    }
}
