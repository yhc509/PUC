using System.IO;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

/// <summary>
/// Redirects <see cref="CliInstallLayout"/> at a throwaway directory so tests never touch the real
/// ~/.unity-cli-bridge install root.
/// </summary>
internal sealed class InstallRootScope : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string? _originalInstallRoot;

    public InstallRootScope()
    {
        _originalInstallRoot = Environment.GetEnvironmentVariable(CliInstallLayout.InstallRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(CliInstallLayout.InstallRootEnvironmentVariable, _temp.Path);
    }

    public string Path => System.IO.Path.GetFullPath(_temp.Path);

    public InstalledCliVersion AddVersion(string version, string protocolVersion)
    {
        string executablePath = AddExecutableOnly(version);
        CliInstallLayout.WriteMeta(CliInstallLayout.GetVersionMetaPath(version), version, protocolVersion);
        return new InstalledCliVersion(
            version,
            protocolVersion,
            CliInstallLayout.GetVersionDirectory(version),
            executablePath);
    }

    /// <summary>
    /// Creates a directory under versions/ with an arbitrary name and arbitrary meta contents, so
    /// tests can reproduce installer staging leftovers and name/meta disagreement.
    /// </summary>
    public void AddRawVersionDirectory(string directoryName, string metaCliVersion, string metaProtocolVersion)
    {
        string directory = System.IO.Path.Combine(CliInstallLayout.GetVersionsDirectory(), directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            System.IO.Path.Combine(directory, CliInstallLayout.GetExecutableFileName()),
            "#!/bin/sh\nexit 0\n");
        File.WriteAllText(
            System.IO.Path.Combine(directory, CliInstallLayout.MetaFileName),
            "{\"cliVersion\":\"" + metaCliVersion + "\",\"protocolVersion\":\"" + metaProtocolVersion + "\"}");
    }

    public string AddExecutableOnly(string version)
    {
        string versionDirectory = CliInstallLayout.GetVersionDirectory(version);
        Directory.CreateDirectory(versionDirectory);
        string executablePath = CliInstallLayout.GetVersionExecutablePath(version);
        File.WriteAllText(executablePath, "#!/bin/sh\nexit 0\n");
        return executablePath;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CliInstallLayout.InstallRootEnvironmentVariable, _originalInstallRoot);
        _temp.Dispose();
    }
}
