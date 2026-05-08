using System.IO;
using System.Text.Json;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class CliAppTests
{
    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);
    private const string GlobalUsage = "usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] <command> [options]";

    [Fact]
    public async Task RunAsync_JsonUnknownCommand_WritesStructuredErrorToStdoutOnly()
    {
        var result = await InvokeAsync(["--json", "stats"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("error", response.status);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("알 수 없는 명령", response.error?.message);

        var details = ParseDetails(response.error?.details);
        Assert.Equal(GlobalUsage, details.GetProperty("usage").GetString());
        Assert.Equal("status", details.GetProperty("suggestions")[0].GetString());
    }

    [Fact]
    public async Task RunAsync_JsonUnknownSubcommand_IncludesSuggestionsAndAvailableCommands()
    {
        var result = await InvokeAsync(["--json", "asset", "delte"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("asset 하위 명령", response.error?.message);

        var details = ParseDetails(response.error?.details);
        Assert.Equal("asset delete", details.GetProperty("suggestions")[0].GetString());
        Assert.Equal("asset create", details.GetProperty("availableCommands")[0].GetString());
    }

    [Fact]
    public async Task RunAsync_JsonMissingRequiredOption_IncludesCommandUsage()
    {
        var result = await InvokeAsync(["--json", "asset", "delete", "--path", "Assets/Test.asset"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("--force", response.error?.message);

        var details = ParseDetails(response.error?.details);
        Assert.Equal(
            "usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] asset delete --path <Assets/...> --force",
            details.GetProperty("usage").GetString());
    }

    [Fact]
    public async Task RunAsync_JsonInvalidOption_IncludesCommandUsage()
    {
        var result = await InvokeAsync(["status", "--json", "--verbose"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("지원하지 않는 옵션", response.error?.message);

        var details = ParseDetails(response.error?.details);
        Assert.Equal(
            "usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] status",
            details.GetProperty("usage").GetString());
    }

    [Fact]
    public async Task RunAsync_CompactUnknownCommand_WritesReducedErrorJsonToStdoutOnly()
    {
        var result = await InvokeAsync(["--output", "compact", "stats"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        JsonElement response = ParseJson(result.Stdout);
        Assert.Equal("CLI_USAGE", response.GetProperty("error").GetString());
        Assert.Contains("알 수 없는 명령", response.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RunAsync_JsonQaSwipeInvalidCoordinate_WritesStructuredUsageError()
    {
        var result = await InvokeAsync(["--json", "qa", "swipe", "--from", "10,right", "--to", "20,30"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("--from", response.error?.message);

        var details = ParseDetails(response.error?.details);
        Assert.Contains("qa swipe", details.GetProperty("usage").GetString());
    }

    [Fact]
    public async Task RunAsync_JsonNoTarget_KeepsFailureStructuredOnStdout()
    {
        var result = await InvokeAsync(["--json", "compile"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("error", response.status);
        Assert.Equal("NO_TARGET", response.error?.code);
        Assert.Equal("cli", response.transport);
    }

    [Fact]
    public async Task RunAsync_Help_ExplainsProjectPathPriority()
    {
        var result = await InvokeAsync(["help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Contains(GlobalUsage, result.Stdout);
        Assert.Contains("--json                Equivalent to --output json. If both --json and --output are specified, the last one wins.", result.Stdout);
        Assert.Contains("--output <mode>       Response format: default, json (full envelope), or compact (data payload / compact error JSON).", result.Stdout);
        Assert.Contains("--project <path|name>  Existing directory paths take precedence over registered project names.", result.Stdout);
        Assert.Contains("Project-name matches are case-insensitive.", result.Stdout);
    }

    [Fact]
    public async Task RunAsync_JsonProjectOverride_DoesNotFallBackToActiveProject()
    {
        using var temp = new TempDirectory();
        string projectRoot = CreateUnityProject(temp.Path, "SampleProject");
        string otherProjectRoot = CreateUnityProject(temp.Path, "OtherProject");
        string otherProjectHash = ProtocolConstants.ComputeProjectHash(otherProjectRoot);
        string expectedProjectHash = ProtocolConstants.ComputeProjectHash(projectRoot);
        string registryContents =
            $$"""
            {"activeProjectHash":"{{otherProjectHash}}","instances":[{"projectRoot":"{{otherProjectRoot.Replace("\\", "\\\\")}}","projectName":"OtherProject","projectHash":"{{otherProjectHash}}","pipeName":"{{ProtocolConstants.BuildPipeName(otherProjectHash).Replace("\\", "\\\\")}}","editorProcessId":1234,"unityVersion":"6000.3.10f1","state":"idle","lastSeenUtc":"2026-04-02T03:19:16.4545650+00:00","capabilities":[]}]}
            """;

        var result = await InvokeAsync(["--json", "--project", projectRoot, "compile"], registryContents: registryContents);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("LIVE_UNAVAILABLE", response.error?.code);
        Assert.Equal(expectedProjectHash, response.target);
        Assert.DoesNotContain(otherProjectHash, result.Stdout);
    }

    [Fact]
    public async Task RunAsync_JsonProjectOverride_ProjectName_UsesRegisteredProjectRoot()
    {
        using var projects = new TempDirectory();
        string projectRoot = CreateUnityProject(projects.Path, "SampleProject");
        string expectedProjectHash = ProtocolConstants.ComputeProjectHash(projectRoot);
        string registryContents =
            $$"""
            {"instances":[{"projectRoot":"{{projectRoot.Replace("\\", "\\\\")}}","projectName":"SampleProject","projectHash":"{{expectedProjectHash}}","pipeName":"{{ProtocolConstants.BuildPipeName(expectedProjectHash).Replace("\\", "\\\\")}}","editorProcessId":1234,"unityVersion":"6000.3.10f1","state":"idle","lastSeenUtc":"2026-04-02T03:19:16.4545650+00:00","capabilities":[]}]}
            """;

        var result = await InvokeAsync(["--json", "--project", "SampleProject", "compile"], registryContents: registryContents);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("LIVE_UNAVAILABLE", response.error?.code);
        Assert.Equal(expectedProjectHash, response.target);
    }

    [Fact]
    public async Task RunAsync_JsonProjectOverride_ProjectName_WhenRegistryDoesNotMatch_ReturnsUsageError()
    {
        const string projectOverride = "UnityCliBridge";
        var result = await InvokeAsync(["--json", "--project", projectOverride, "compile"], registryContents: "{\"instances\":[]}");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Equal(
            "'UnityCliBridge' is not a registered project name or a valid directory path. Run 'unity-cli instances list' to see registered projects.",
            response.error?.message);
    }

    [Fact]
    public async Task RunAsync_JsonProjectOverride_DotPath_UsesCurrentProjectPath()
    {
        using var temp = new TempDirectory();
        string projectRoot = CreateUnityProject(temp.Path, "SampleProject");
        string expectedProjectHash = ProtocolConstants.ComputeProjectHash(projectRoot);

        var result = await InvokeAsync(["--json", "--project", ".", "compile"], currentDirectory: projectRoot);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("LIVE_UNAVAILABLE", response.error?.code);
        Assert.Equal(expectedProjectHash, response.target);
    }

    [Fact]
    public async Task RunAsync_JsonProjectOverride_ProjectName_WhenAmbiguous_ReturnsUsageError()
    {
        using var projects = new TempDirectory();
        string firstProjectRoot = CreateUnityProject(projects.Path, "SampleProjectA");
        string secondProjectRoot = CreateUnityProject(projects.Path, "SampleProjectB");
        string registryContents =
            $$"""
            {"instances":[{"projectRoot":"{{firstProjectRoot.Replace("\\", "\\\\")}}","projectName":"UnityCliBridge","projectHash":"{{ProtocolConstants.ComputeProjectHash(firstProjectRoot)}}","pipeName":"{{ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(firstProjectRoot)).Replace("\\", "\\\\")}}","editorProcessId":1234,"unityVersion":"6000.3.10f1","state":"idle","lastSeenUtc":"2026-04-02T03:19:16.4545650+00:00","capabilities":[]},{"projectRoot":"{{secondProjectRoot.Replace("\\", "\\\\")}}","projectName":"UnityCliBridge","projectHash":"{{ProtocolConstants.ComputeProjectHash(secondProjectRoot)}}","pipeName":"{{ProtocolConstants.BuildPipeName(ProtocolConstants.ComputeProjectHash(secondProjectRoot)).Replace("\\", "\\\\")}}","editorProcessId":5678,"unityVersion":"6000.3.10f1","state":"idle","lastSeenUtc":"2026-04-02T03:19:16.4545650+00:00","capabilities":[]}]}
            """;

        var result = await InvokeAsync(["--json", "--project", "UnityCliBridge", "compile"], registryContents: registryContents);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("중복되어", response.error?.message);
        Assert.Contains(ProtocolConstants.GetCanonicalPath(firstProjectRoot), response.error?.message);
        Assert.Contains(ProtocolConstants.GetCanonicalPath(secondProjectRoot), response.error?.message);
    }

    [Fact]
    public async Task RunAsync_JsonProjectOverride_PathTakesPrecedenceOverRegisteredProjectName()
    {
        using var projects = new TempDirectory();
        string pathProjectRoot = CreateUnityProject(projects.Path, "UnityCliBridge");
        string registeredProjectRoot = CreateUnityProject(projects.Path, "RegisteredProject");
        string pathProjectHash = ProtocolConstants.ComputeProjectHash(pathProjectRoot);
        string registeredProjectHash = ProtocolConstants.ComputeProjectHash(registeredProjectRoot);
        string registryContents =
            $$"""
            {"instances":[{"projectRoot":"{{registeredProjectRoot.Replace("\\", "\\\\")}}","projectName":"UnityCliBridge","projectHash":"{{registeredProjectHash}}","pipeName":"{{ProtocolConstants.BuildPipeName(registeredProjectHash).Replace("\\", "\\\\")}}","editorProcessId":1234,"unityVersion":"6000.3.10f1","state":"idle","lastSeenUtc":"2026-04-02T03:19:16.4545650+00:00","capabilities":[]}]}
            """;

        var result = await InvokeAsync(
            ["--json", "--project", "UnityCliBridge", "compile"],
            registryContents: registryContents,
            currentDirectory: projects.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("LIVE_UNAVAILABLE", response.error?.code);
        Assert.Equal(pathProjectHash, response.target);
        Assert.NotEqual(registeredProjectHash, response.target);
    }

    [Fact]
    public async Task RunAsync_JsonInstancesList_ReportsActiveAndCurrentProjectRoot()
    {
        using var projects = new TempDirectory();
        string projectRoot = CreateUnityProject(projects.Path, "SampleProject");
        string canonicalProjectRoot = ProtocolConstants.GetCanonicalPath(projectRoot);
        string projectHash = ProtocolConstants.ComputeProjectHash(canonicalProjectRoot);
        string registryContents =
            $$"""
            {"activeProjectRoot":"{{canonicalProjectRoot.Replace("\\", "\\\\")}}","instances":[{"projectRoot":"{{canonicalProjectRoot.Replace("\\", "\\\\")}}","projectName":"SampleProject","projectHash":"{{projectHash}}","pipeName":"{{ProtocolConstants.BuildPipeName(projectHash).Replace("\\", "\\\\")}}","editorProcessId":1234,"unityVersion":"6000.3.10f1","state":"idle","lastSeenUtc":"{{DateTimeOffset.UtcNow.ToString("O")}}","capabilities":[]}]}
            """;

        var result = await InvokeAsync(["--json", "instances", "list"], registryContents: registryContents, currentDirectory: projectRoot);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.True(response.data.HasValue);
        JsonElement data = response.data.Value;
        Assert.Equal(canonicalProjectRoot, data.GetProperty("activeProjectRoot").GetString());
        Assert.Equal(canonicalProjectRoot, data.GetProperty("currentProjectRoot").GetString());
        Assert.Equal(projectHash, data.GetProperty("currentProjectHash").GetString());
        Assert.False(data.TryGetProperty("activeProjectHash", out _));
    }

    [Fact]
    public async Task RunAsync_JsonInstancesUse_ProjectName_WhenRegistryDoesNotMatch_ReturnsUsageError()
    {
        using var temp = new TempDirectory();
        string fallbackProjectRoot = CreateUnityProject(temp.Path, "FallbackProject");
        var result = await InvokeAsync(
            ["--json", "instances", "use", "TypoProject"],
            registryContents: "{\"instances\":[]}",
            currentDirectory: fallbackProjectRoot);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Equal(
            "'TypoProject' is not a known project hash, a registered project name, or a valid directory path. Run 'unity-cli instances list' to see registered projects.",
            response.error?.message);
    }

    [Fact]
    public async Task RunAsync_JsonUnexpectedException_WritesCliErrorToStdoutOnly()
    {
        var result = await InvokeAsync(
            ["--json", "compile"],
            registryContents: "{\"instances\":");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_ERROR", response.error?.code);
        Assert.Contains("JsonException", response.error?.details?.GetString());
    }

    [Fact]
    public async Task RunAsync_JsonRawPayloadValidation_IsNotMaskedByNoTarget()
    {
        var result = await InvokeAsync(["--json", "raw", "--json", "{"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var response = ParseResponse(result.Stdout);
        Assert.Equal("CLI_USAGE", response.error?.code);
        Assert.Contains("올바른 JSON", response.error?.message);

        var details = ParseDetails(response.error?.details);
        Assert.Equal(
            "usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] raw [--force] --json '{\"command\":\"status\",\"arguments\":{}}'",
            details.GetProperty("usage").GetString());
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ShowsSuggestionInStderr()
    {
        var result = await InvokeAsync(["stats"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("알 수 없는 명령입니다: stats", result.Stderr);
        Assert.Contains("유사한 명령:", result.Stderr);
        Assert.Contains("status", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_UnknownSubcommand_ShowsAvailableChoicesInStderr()
    {
        var result = await InvokeAsync(["asset", "delte"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("알 수 없는 asset 하위 명령입니다: delte", result.Stderr);
        Assert.Contains("유사한 `asset` 하위 명령:", result.Stderr);
        Assert.Contains("asset delete", result.Stderr);
        Assert.Contains("사용 가능한 `asset` 하위 명령:", result.Stderr);
        Assert.Contains("asset create", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_MissingRequiredOption_ShowsCommandUsageInStderr()
    {
        var result = await InvokeAsync(["asset", "delete", "--path", "Assets/Test.asset"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("--force", result.Stderr);
        Assert.Contains(
            "usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] asset delete --path <Assets/...> --force",
            result.Stderr);
    }

    [Fact]
    public async Task RunAsync_InvalidOption_ShowsCommandUsageInStderr()
    {
        var result = await InvokeAsync(["status", "--verbose"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("지원하지 않는 옵션입니다: --verbose", result.Stderr);
        Assert.Contains("usage: unity-cli [--json] [--output <default|json|compact>] [--project <path|name>] status", result.Stderr);
    }

    [Fact]
    public void BuildHelpText_DescribesProjectPathPrecedence()
    {
        var helpText = CliCommandMetadata.BuildHelpText();

        Assert.Contains(GlobalUsage, helpText);
        Assert.Contains("--json                Equivalent to --output json. If both --json and --output are specified, the last one wins.", helpText);
        Assert.Contains("Existing directory paths take precedence over registered project names.", helpText);
        Assert.Contains("Project-name matches are case-insensitive.", helpText);
    }

    private static ResponseEnvelope ParseResponse(string stdout)
    {
        Assert.False(string.IsNullOrWhiteSpace(stdout));
        return ProtocolJson.Deserialize<ResponseEnvelope>(stdout.Trim());
    }

    private static JsonElement ParseDetails(JsonElement? details)
    {
        Assert.True(details.HasValue);
        return details.Value;
    }

    private static JsonElement ParseJson(string json)
    {
        Assert.False(string.IsNullOrWhiteSpace(json));
        return JsonSerializer.Deserialize<JsonElement>(json.Trim());
    }

    private static async Task<CliInvocationResult> InvokeAsync(
        string[] args,
        string? registryContents = null,
        string? currentDirectory = null)
    {
        await ConsoleLock.WaitAsync();

        try
        {
            using var temp = new TempDirectory();
            string registryPath = Path.Combine(temp.Path, "instances.json");
            if (registryContents is not null)
            {
                File.WriteAllText(registryPath, registryContents);
            }

            string? originalRegistryPath = Environment.GetEnvironmentVariable("UNITY_CLI_REGISTRY_PATH");
            string originalCurrentDirectory = Environment.CurrentDirectory;
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            try
            {
                Environment.SetEnvironmentVariable("UNITY_CLI_REGISTRY_PATH", registryPath);
                Environment.CurrentDirectory = currentDirectory ?? temp.Path;
                Console.SetOut(stdout);
                Console.SetError(stderr);

                int exitCode = await UnityCli.Cli.CliApp.RunAsync(args);
                return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                Environment.CurrentDirectory = originalCurrentDirectory;
                Environment.SetEnvironmentVariable("UNITY_CLI_REGISTRY_PATH", originalRegistryPath);
            }
        }
        finally
        {
            ConsoleLock.Release();
        }
    }

    private static string CreateUnityProject(string root, string name)
    {
        string projectRoot = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Packages"));
        return projectRoot;
    }

    private sealed record CliInvocationResult(int ExitCode, string Stdout, string Stderr);
}
