using UnityCli.Cli.Models;
using UnityCli.Cli.Services;

namespace UnityCli.Cli.Tests;

public sealed class EditorLauncherTests
{
    [Fact]
    public void BuildLaunchArguments_DefaultIsHeadlessWithGpu()
    {
        var parsed = new ParsedCommand(CommandKind.EditorLaunch);

        string[] args = EditorLauncher.BuildLaunchArguments(parsed, "/proj/root", "/proj/root/Library/ucli-launch.log");

        Assert.Contains("-batchmode", args);
        Assert.DoesNotContain("-nographics", args);
        Assert.Contains("-projectPath", args);
        Assert.Contains("/proj/root", args);
    }

    [Fact]
    public void BuildLaunchArguments_GuiOmitsBatchmode()
    {
        var parsed = new ParsedCommand(CommandKind.EditorLaunch) { EditorGui = true };

        string[] args = EditorLauncher.BuildLaunchArguments(parsed, "/proj/root", "/log");

        Assert.DoesNotContain("-batchmode", args);
        Assert.DoesNotContain("-nographics", args);
    }

    [Fact]
    public void BuildLaunchArguments_NographicsAddsFlag()
    {
        var parsed = new ParsedCommand(CommandKind.EditorLaunch) { EditorNoGraphics = true };

        string[] args = EditorLauncher.BuildLaunchArguments(parsed, "/proj/root", "/log");

        Assert.Contains("-batchmode", args);
        Assert.Contains("-nographics", args);
    }

    [Fact]
    public void BuildStartInfo_OnUnix_DetachesStdioViaShExec()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var startInfo = EditorLauncher.BuildStartInfo("/Applications/Unity/Unity", new[] { "-projectPath", "/proj/root", "-batchmode" });

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.Equal("-c", startInfo.ArgumentList[0]);
        Assert.Contains(">/dev/null", startInfo.ArgumentList[1]);
        Assert.Contains("exec", startInfo.ArgumentList[1]);
        Assert.Equal("/Applications/Unity/Unity", startInfo.ArgumentList[2]);
        Assert.Equal("-projectPath", startInfo.ArgumentList[3]);
        Assert.Equal("/proj/root", startInfo.ArgumentList[4]);
        Assert.Equal("-batchmode", startInfo.ArgumentList[5]);
    }

    [Fact]
    public void RequestedModeLabel_MapsFlags()
    {
        Assert.Equal("gui", EditorLauncher.RequestedModeLabel(new ParsedCommand(CommandKind.EditorLaunch) { EditorGui = true }));
        Assert.Equal("headless", EditorLauncher.RequestedModeLabel(new ParsedCommand(CommandKind.EditorLaunch)));
        Assert.Equal("headless-nographics", EditorLauncher.RequestedModeLabel(new ParsedCommand(CommandKind.EditorLaunch) { EditorNoGraphics = true }));
    }

    [Theory]
    [InlineData("75188 /Applications/Unity/Hub/Editor/6000.3.10f1/Unity.app/Contents/MacOS/Unity -projectpath /proj/root -useHub", true)]
    [InlineData("77545 /Applications/Unity/.../Unity -adb2 -batchMode -name AssetImportWorker0 -projectPath /proj/root", false)]
    [InlineData("737 /Applications/Unity Hub.app/.../Unity Hub Helper --type=renderer --hub-startup-projects=[{\"path\":\"/proj/root\"}]", false)]
    [InlineData("75330 /Applications/Unity/Hub/Editor/6000.3.10f1/Unity.app/Contents/MacOS/Unity -projectpath /other/project", false)]
    public void IsMainEditorProcessLine_FiltersFalsePositives(string psLine, bool expected)
    {
        Assert.Equal(expected, EditorLauncher.IsMainEditorProcessLine(psLine, "/proj/root"));
    }
}
