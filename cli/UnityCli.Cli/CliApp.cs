using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli;

public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var outputMode = CliCommandMetadata.DetectOutputMode(args);
        ParsedCommand? parsed = null;

        try
        {
            parsed = CliArgumentParser.Parse(args);
            outputMode = parsed.OutputMode;
            if (parsed.Kind == CommandKind.Help)
            {
                Console.WriteLine(CliArgumentParser.BuildHelpText());
                return 0;
            }

            var registryStore = new InstanceRegistryStore();
            var locator = new UnityProjectLocator();
            var projectRoot = ResolveProjectRoot(parsed, locator, registryStore);

            var response = parsed.Kind switch
            {
                CommandKind.Status => await RunStatusAsync(registryStore, projectRoot),
                CommandKind.InstancesList => ListInstances(registryStore, projectRoot),
                CommandKind.InstancesUse => UseInstance(registryStore, parsed),
                CommandKind.Doctor => await RunDoctorAsync(registryStore, locator, parsed, projectRoot),
                CommandKind.QaWait => await RunQaWait(parsed),
                _ => await ExecuteUnityCommandAsync(parsed, registryStore, projectRoot),
            };

            Console.WriteLine(ResponseFormatter.Format(parsed.OutputMode, response));
            return response.status == "success" ? 0 : 1;
        }
        catch (CliUsageException ex)
        {
            var presentation = CliUsageHelp.Build(args, ex.Message, parsed);
            var response = ResponseEnvelope.Failure(
                Guid.NewGuid().ToString("N"),
                null,
                "CLI_USAGE",
                presentation.Message,
                retryable: false,
                details: presentation.BuildDetailsJson(),
                transport: "cli");

            WriteUsageError(outputMode, response, presentation);
            return 2;
        }
        catch (Exception ex)
        {
            var response = ResponseEnvelope.Failure(
                Guid.NewGuid().ToString("N"),
                null,
                "CLI_ERROR",
                ex.Message,
                retryable: false,
                details: ex.ToString(),
                transport: "cli");

            WriteErrorResponse(outputMode, response);
            return 1;
        }
    }

    private static string? ResolveProjectRoot(
        ParsedCommand parsed,
        UnityProjectLocator locator,
        InstanceRegistryStore registryStore)
    {
        if (!string.IsNullOrWhiteSpace(parsed.ProjectOverride))
        {
            var projectOverride = parsed.ProjectOverride.Trim();
            if (Directory.Exists(projectOverride))
            {
                return ProtocolConstants.GetCanonicalPath(projectOverride);
            }

            var registry = registryStore.Load();
            return registryStore.ResolveProjectRootOverride(registry, projectOverride);
        }

        return locator.TryFindProjectRoot(Environment.CurrentDirectory);
    }

    private static ResponseEnvelope ListInstances(InstanceRegistryStore registryStore, string? projectRoot)
    {
        var registry = registryStore.Load();
        var activeHash = registry.activeProjectHash;
        var data = new
        {
            activeProjectHash = activeHash,
            currentProjectHash = !string.IsNullOrWhiteSpace(projectRoot) ? ProtocolConstants.ComputeProjectHash(projectRoot) : null,
            instances = registry.instances
                .OrderBy(item => item.projectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.projectRoot, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };

        return ResponseEnvelope.Success(
            Guid.NewGuid().ToString("N"),
            null,
            CreateDataElement(data),
            durationMs: 0,
            transport: "cli");
    }

    private static ResponseEnvelope UseInstance(InstanceRegistryStore registryStore, ParsedCommand parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.InstanceTarget))
        {
            throw new CliUsageException("`instances use`에는 project hash, project path 또는 project name이 필요합니다.");
        }

        var registry = registryStore.Load();
        var target = registryStore.ResolveOrCreateTarget(registry, parsed.InstanceTarget!);
        registry.activeProjectHash = target.projectHash;
        registryStore.Save(registry);

        var data = new
        {
            activeProjectHash = target.projectHash,
            target.projectName,
            target.projectRoot,
            target.pipeName,
            target.state,
        };

        return ResponseEnvelope.Success(
            Guid.NewGuid().ToString("N"),
            target.projectHash,
            CreateDataElement(data),
            durationMs: 0,
            transport: "cli");
    }

    private static async Task<ResponseEnvelope> RunStatusAsync(InstanceRegistryStore registryStore, string? projectRoot)
    {
        var registry = registryStore.Load();
        var target = ResolveTarget(registry, projectRoot);
        if (target is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(5_000);
                return await new LocalIpcClient().SendAsync(
                    target,
                    new CommandEnvelope
                    {
                        requestId = Guid.NewGuid().ToString("N"),
                        command = ProtocolConstants.CommandStatus,
                        argumentsJson = "{}",
                    },
                    5_000,
                    cts.Token);
            }
            catch
            {
            }
        }

        var data = new
        {
            projectRoot,
            projectHash = !string.IsNullOrWhiteSpace(projectRoot) ? ProtocolConstants.ComputeProjectHash(projectRoot) : null,
            activeProjectHash = registry.activeProjectHash,
            liveReachable = false,
            unityPath = !string.IsNullOrWhiteSpace(projectRoot) ? UnityEditorLocator.TryResolve(projectRoot) : null,
            registryPath = RegistryPathUtility.GetRegistryFilePath(),
        };

        return ResponseEnvelope.Success(
            Guid.NewGuid().ToString("N"),
            target?.projectHash,
            CreateDataElement(data),
            durationMs: 0,
            transport: "cli");
    }

    private static async Task<ResponseEnvelope> RunDoctorAsync(
        InstanceRegistryStore registryStore,
        UnityProjectLocator locator,
        ParsedCommand parsed,
        string? projectRoot)
    {
        var registry = registryStore.Load();
        var target = ResolveTarget(registry, projectRoot);
        var unityPath = !string.IsNullOrWhiteSpace(projectRoot)
            ? UnityEditorLocator.TryResolve(projectRoot)
            : null;

        var liveReachable = false;
        if (target is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(5_000);
                var ipcClient = new LocalIpcClient();
                var ping = new CommandEnvelope
                {
                    requestId = Guid.NewGuid().ToString("N"),
                    command = "ping",
                    argumentsJson = "{}",
                };
                var response = await ipcClient.SendAsync(target, ping, 5_000, cts.Token);
                liveReachable = response.status == "success";
            }
            catch
            {
                liveReachable = false;
            }
        }

        var data = new
        {
            registryPath = RegistryPathUtility.GetRegistryFilePath(),
            workingDirectory = Environment.CurrentDirectory,
            projectRoot,
            projectDetectedFromChildren = string.IsNullOrWhiteSpace(projectRoot) ? locator.TryFindProjectRoot(Environment.CurrentDirectory) : projectRoot,
            targetProjectHash = target?.projectHash,
            targetProjectName = target?.projectName,
            pipeName = target?.pipeName,
            liveReachable,
            unityPath,
            instanceCount = registry.instances.Length,
        };

        return ResponseEnvelope.Success(
            Guid.NewGuid().ToString("N"),
            target?.projectHash,
            CreateDataElement(data),
            durationMs: 0,
            transport: "cli");
    }

    private static async Task<ResponseEnvelope> ExecuteUnityCommandAsync(
        ParsedCommand parsed,
        InstanceRegistryStore registryStore,
        string? projectRoot)
    {
        var command = parsed.ToEnvelope();
        var registry = registryStore.Load();
        var target = ResolveTarget(registry, projectRoot);

        if (target is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(parsed.TimeoutMs);
                return await new LocalIpcClient().SendAsync(target, command, parsed.TimeoutMs, cts.Token);
            }
            catch (Exception ex)
            {
                return CreateLiveUnavailableResponse(
                    target.projectHash,
                    "Unity가 로컬 패키지를 import/compile 중인지 확인한 뒤 다시 시도하세요. 원인: " + ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            return CreateLiveUnavailableResponse(
                ProtocolConstants.ComputeProjectHash(projectRoot),
                "Unity Editor를 열고 Bridge import/compile이 끝난 뒤 다시 시도하세요.");
        }

        return ResponseEnvelope.Failure(
            Guid.NewGuid().ToString("N"),
            target?.projectHash,
            "NO_TARGET",
            "Unity Editor가 실행 중이지 않거나 Bridge가 활성화되지 않았습니다.",
            retryable: false,
            transport: "cli",
            details: "Unity 프로젝트 루트에서 실행하거나 `unity-cli instances use <projectHash|projectPath|projectName>`로 대상을 고정하세요.");
    }

    private static async Task<ResponseEnvelope> RunQaWait(ParsedCommand parsed)
    {
        if (parsed.QaWaitMs <= 0)
        {
            throw new CliUsageException("qa wait에는 --ms <밀리초>가 필요합니다.");
        }

        await Task.Delay(parsed.QaWaitMs);
        var payload = new { waited = true, ms = parsed.QaWaitMs };
        return ResponseEnvelope.Success(
            Guid.NewGuid().ToString("N"),
            null,
            CreateDataElement(payload),
            parsed.QaWaitMs,
            "cli");
    }

    private static JsonElement CreateDataElement(object data)
    {
        return JsonSerializer.SerializeToElement(data, ProtocolJson.Default);
    }

    private static ResponseEnvelope CreateLiveUnavailableResponse(string? projectHash, string? details)
    {
        return ResponseEnvelope.Failure(
            Guid.NewGuid().ToString("N"),
            projectHash,
            "LIVE_UNAVAILABLE",
            "Unity Editor가 실행 중이지 않거나 Bridge가 활성화되지 않았습니다.",
            retryable: true,
            transport: "cli",
            details: details);
    }

    private static InstanceRecord? ResolveTarget(InstanceRegistry registry, string? projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var canonicalProjectRoot = ProtocolConstants.GetCanonicalPath(projectRoot);
            var projectHash = ProtocolConstants.ComputeProjectHash(canonicalProjectRoot);
            var match = registry.instances.FirstOrDefault(item => item.projectHash == projectHash);
            if (match is not null)
            {
                return match;
            }

            return new InstanceRecord
            {
                projectRoot = canonicalProjectRoot,
                projectName = Path.GetFileName(canonicalProjectRoot),
                projectHash = projectHash,
                pipeName = ProtocolConstants.BuildPipeName(projectHash),
                state = "offline",
                lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                capabilities = Array.Empty<string>(),
            };
        }

        if (!string.IsNullOrWhiteSpace(registry.activeProjectHash))
        {
            return registry.instances.FirstOrDefault(item => item.projectHash == registry.activeProjectHash);
        }

        return null;
    }

    private static void WriteErrorResponse(OutputMode outputMode, ResponseEnvelope response)
    {
        if (outputMode != OutputMode.Default)
        {
            Console.Out.WriteLine(ResponseFormatter.Format(outputMode, response));
            return;
        }

        if (response.error is not null)
        {
            Console.Error.WriteLine(response.error.message);

            if (response.error.details is JsonElement details && details.ValueKind != JsonValueKind.Null)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(details.ToString());
            }

            return;
        }

        Console.Error.WriteLine(ResponseFormatter.Format(OutputMode.Default, response));
    }

    private static void WriteUsageError(OutputMode outputMode, ResponseEnvelope response, CliUsagePresentation presentation)
    {
        if (outputMode != OutputMode.Default)
        {
            Console.Out.WriteLine(ResponseFormatter.Format(outputMode, response));
            return;
        }

        CliUsageHelp.WriteTo(Console.Error, presentation);
    }
}
