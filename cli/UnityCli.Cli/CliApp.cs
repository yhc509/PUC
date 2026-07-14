using System.Text.Json;
using System.Text.Json.Nodes;
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
                CommandKind.InstancesList => ListInstances(registryStore, projectRoot, parsed),
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

    private static ResponseEnvelope ListInstances(InstanceRegistryStore registryStore, string? projectRoot, ParsedCommand parsed)
    {
        var registry = registryStore.Load();
        var canonicalCurrent = !string.IsNullOrWhiteSpace(projectRoot)
            ? ProtocolConstants.GetCanonicalPath(projectRoot)
            : null;
        var sortedInstances = registry.instances
            .OrderBy(item => item.projectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.projectRoot, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        object data = parsed.InstancesBrief
            ? new
            {
                activeProjectRoot = registry.activeProjectRoot,
                currentProjectRoot = canonicalCurrent,
                currentProjectHash = canonicalCurrent != null ? ProtocolConstants.ComputeProjectHash(canonicalCurrent) : null,
                instances = sortedInstances.Select(item => new
                {
                    item.projectName,
                    item.projectRoot,
                    item.projectHash,
                    item.state,
                }).ToArray(),
            }
            : new
            {
                activeProjectRoot = registry.activeProjectRoot,
                currentProjectRoot = canonicalCurrent,
                currentProjectHash = canonicalCurrent != null ? ProtocolConstants.ComputeProjectHash(canonicalCurrent) : null,
                instances = sortedInstances,
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
        registry.activeProjectRoot = target.projectRoot;
        registry.activeProjectRootPinned = true;
        registry.activeProjectHash = null;
        registryStore.Save(registry);

        var data = new
        {
            activeProjectRoot = target.projectRoot,
            target.projectName,
            target.projectRoot,
            target.projectHash,
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
        if (target is not null && !IsMissingAuthTokenForSyntheticTarget(registry, projectRoot, target))
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
            activeProjectRoot = registry.activeProjectRoot,
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
        string? liveErrorCode = null;
        string? liveErrorMessage = null;
        if (target is not null && !IsMissingAuthTokenForSyntheticTarget(registry, projectRoot, target))
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
                if (!liveReachable && response.error is not null)
                {
                    liveErrorCode = response.error.code;
                    liveErrorMessage = response.error.message;
                }
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
            activeProjectRoot = registry.activeProjectRoot,
            targetProjectHash = target?.projectHash,
            targetProjectName = target?.projectName,
            pipeName = target?.pipeName,
            liveReachable,
            liveErrorCode,
            liveErrorMessage,
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

    internal static async Task<ResponseEnvelope> ExecuteUnityCommandAsync(
        ParsedCommand parsed,
        InstanceRegistryStore registryStore,
        string? projectRoot,
        Func<InstanceRecord, CommandEnvelope, int, CancellationToken, Task<ResponseEnvelope>>? sendAsync = null)
    {
        var command = parsed.ToEnvelope();
        var registry = registryStore.Load();
        var target = ResolveTarget(registry, projectRoot);

        if (target is not null)
        {
            if (IsMissingAuthTokenForSyntheticTarget(registry, projectRoot, target))
            {
                return CreateLiveUnavailableResponse(
                    target.projectHash,
                    "Bridge 인증 정보를 읽지 못했습니다. Unity Editor가 실행 중이 아니거나, 시작·재시작·스크립트 재컴파일 중일 수 있습니다. Editor가 떠 있다면 잠시 후 다시 시도하세요.");
            }

            try
            {
                int liveTimeoutMs = ResolveLiveTimeoutMs(parsed);
                using var cts = new CancellationTokenSource(ResolveCommandCancellationTimeoutMs(parsed, liveTimeoutMs));
                var ipcClient = new LocalIpcClient();
                var sendCommandAsync = sendAsync ?? ipcClient.SendAsync;
                var response = await sendCommandAsync(target, command, liveTimeoutMs, cts.Token);
                if (IsUnauthorizedResponse(response))
                {
                    (response, target) = await RetryUnauthorizedOnceAsync(
                        registryStore,
                        projectRoot,
                        target,
                        command,
                        liveTimeoutMs,
                        cts.Token,
                        sendCommandAsync,
                        response);
                }

                if (ShouldPollTestResults(parsed, response))
                {
                    return await PollTestResultsAsync(parsed, target, ipcClient, response, cts.Token);
                }

                if (ShouldPollRecordStatus(parsed, response))
                {
                    return await PollRecordStatusAsync(parsed, target, sendCommandAsync, response, cts.Token);
                }

                if (ShouldPollEditorReady(parsed, response))
                {
                    TimeSpan waitTimeout = ResolveEditorReadyWaitTimeout(parsed);
                    return await PollEditorReadyAsync(
                        parsed,
                        response,
                        (statusCommand, timeoutMs, cancellationToken) =>
                            sendCommandAsync(target, statusCommand, timeoutMs, cancellationToken),
                        cts.Token,
                        timeout: waitTimeout);
                }

                return NormalizeResultEnvelope(parsed.Kind, response);
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

        var offlineCandidates = registry.instances
            .OrderBy(item => item.projectName, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"  - {item.projectName} ({item.state})  {item.projectRoot}")
            .ToArray();
        string noTargetDetails = offlineCandidates.Length > 0
            ? "Unity 프로젝트 루트에서 실행하거나 `unity-cli instances use <projectHash|projectPath|projectName>`로 대상을 고정하세요.\n"
              + "등록됐으나 실행 중이 아닌 인스턴스:\n" + string.Join("\n", offlineCandidates)
            : "Unity 프로젝트 루트에서 실행하거나 `unity-cli instances use <projectHash|projectPath|projectName>`로 대상을 고정하세요.";

        return ResponseEnvelope.Failure(
            Guid.NewGuid().ToString("N"),
            target?.projectHash,
            "NO_TARGET",
            "Unity Editor가 실행 중이지 않거나 Bridge가 활성화되지 않았습니다.",
            retryable: false,
            transport: "cli",
            details: noTargetDetails);
    }

    private static async Task<(ResponseEnvelope Response, InstanceRecord Target)> RetryUnauthorizedOnceAsync(
        InstanceRegistryStore registryStore,
        string? projectRoot,
        InstanceRecord originalTarget,
        CommandEnvelope command,
        int liveTimeoutMs,
        CancellationToken cancellationToken,
        Func<InstanceRecord, CommandEnvelope, int, CancellationToken, Task<ResponseEnvelope>> sendCommandAsync,
        ResponseEnvelope unauthorizedResponse)
    {
        InstanceRecord? refreshedTarget = ResolveSameTargetForUnauthorizedRetry(
            registryStore.Load(),
            projectRoot,
            originalTarget);
        if (refreshedTarget is null
            || string.Equals(refreshedTarget.token, originalTarget.token, StringComparison.Ordinal))
        {
            return (AddUnauthorizedRetryHint(unauthorizedResponse), originalTarget);
        }

        var retryResponse = await sendCommandAsync(refreshedTarget, command, liveTimeoutMs, cancellationToken);
        retryResponse = IsUnauthorizedResponse(retryResponse)
            ? AddUnauthorizedRetryHint(retryResponse)
            : retryResponse;
        return (retryResponse, refreshedTarget);
    }

    private static InstanceRecord? ResolveSameTargetForUnauthorizedRetry(
        InstanceRegistry registry,
        string? projectRoot,
        InstanceRecord originalTarget)
    {
        registry.instances ??= Array.Empty<InstanceRecord>();

        string? targetRoot = !string.IsNullOrWhiteSpace(originalTarget.projectRoot)
            ? ProtocolConstants.GetCanonicalPath(originalTarget.projectRoot)
            : !string.IsNullOrWhiteSpace(projectRoot)
                ? ProtocolConstants.GetCanonicalPath(projectRoot)
                : null;

        if (!string.IsNullOrWhiteSpace(targetRoot))
        {
            var rootMatch = registry.instances.FirstOrDefault(item =>
                string.Equals(
                    ProtocolConstants.GetCanonicalPath(item.projectRoot),
                    targetRoot,
                    StringComparison.OrdinalIgnoreCase));
            if (rootMatch is not null)
            {
                return rootMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(originalTarget.projectHash))
        {
            var hashMatch = registry.instances.FirstOrDefault(item =>
                string.Equals(item.projectHash, originalTarget.projectHash, StringComparison.OrdinalIgnoreCase));
            if (hashMatch is not null)
            {
                return hashMatch;
            }
        }

        return string.IsNullOrWhiteSpace(originalTarget.pipeName)
            ? null
            : registry.instances.FirstOrDefault(item =>
                string.Equals(item.pipeName, originalTarget.pipeName, StringComparison.Ordinal));
    }

    private static bool IsMissingAuthTokenForSyntheticTarget(
        InstanceRegistry registry,
        string? projectRoot,
        InstanceRecord target)
    {
        if (!string.IsNullOrEmpty(target.token) || string.IsNullOrWhiteSpace(projectRoot))
        {
            return false;
        }

        registry.instances ??= Array.Empty<InstanceRecord>();
        string canonicalProjectRoot = ProtocolConstants.GetCanonicalPath(projectRoot);
        return !registry.instances.Any(item =>
            !string.IsNullOrWhiteSpace(item.projectRoot) &&
            string.Equals(
                ProtocolConstants.GetCanonicalPath(item.projectRoot),
                canonicalProjectRoot,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnauthorizedResponse(ResponseEnvelope response)
    {
        return string.Equals(response.error?.code, ProtocolConstants.ErrorUnauthorized, StringComparison.Ordinal);
    }

    private static ResponseEnvelope AddUnauthorizedRetryHint(ResponseEnvelope response)
    {
        if (response.error is null)
        {
            return response;
        }

        string hint = " Editor may have restarted; retry after the registry heartbeat refreshes (~"
            + ProtocolConstants.RegistryHeartbeatSeconds
            + " seconds).";
        if (!response.error.message.Contains("registry heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            response.error.message = response.error.message.TrimEnd() + hint;
        }

        return response;
    }

    private static int ResolveLiveTimeoutMs(ParsedCommand parsed)
    {
        if (parsed.Kind != CommandKind.TestRun)
        {
            return parsed.TimeoutMs;
        }

        int timeoutSeconds = parsed.TestTimeoutSeconds ?? ProtocolConstants.DefaultTestRunTimeoutSeconds;
        int testTimeoutMs = ((timeoutSeconds + ProtocolConstants.TestRunCancelGraceSeconds) * 1000)
            + ProtocolConstants.DefaultLiveTimeoutMs;
        return Math.Max(parsed.TimeoutMs, testTimeoutMs);
    }

    private static int ResolveCommandCancellationTimeoutMs(ParsedCommand parsed, int liveTimeoutMs)
    {
        if (parsed.Wait && parsed.Kind is CommandKind.Compile or CommandKind.Refresh)
        {
            double totalMs = liveTimeoutMs + ResolveEditorReadyWaitTimeout(parsed).TotalMilliseconds;
            return totalMs >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(totalMs));
        }

        if (parsed.Kind == CommandKind.RecordStart && parsed.RecordWait)
        {
            int durationSeconds = parsed.RecordDuration ?? ProtocolConstants.MaxRecordDurationSeconds;
            double totalMs = Math.Max(liveTimeoutMs, (durationSeconds + 15) * 1000d);
            return totalMs >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(totalMs));
        }

        return liveTimeoutMs;
    }

    private static bool ShouldPollTestResults(ParsedCommand parsed, ResponseEnvelope response)
    {
        return parsed.Kind == CommandKind.TestRun
            && parsed.TestWait
            && string.Equals(parsed.TestMode, "play", StringComparison.Ordinal)
            && string.Equals(response.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal);
    }

    internal static bool ShouldPollRecordStatus(ParsedCommand parsed, ResponseEnvelope response)
    {
        return parsed.Kind == CommandKind.RecordStart
            && parsed.RecordWait
            && string.Equals(response.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal);
    }

    private static bool ShouldPollEditorReady(ParsedCommand parsed, ResponseEnvelope response)
    {
        return parsed.Wait
            && parsed.Kind is CommandKind.Compile or CommandKind.Refresh
            && string.Equals(response.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal);
    }

    private static TimeSpan ResolveEditorReadyWaitTimeout(ParsedCommand parsed)
    {
        return parsed.TimeoutMsSpecified
            ? TimeSpan.FromMilliseconds(parsed.TimeoutMs)
            : TimeSpan.FromSeconds(ProtocolConstants.DefaultCompileRefreshWaitTimeoutSeconds);
    }

    private static async Task<ResponseEnvelope> PollTestResultsAsync(
        ParsedCommand original,
        InstanceRecord target,
        LocalIpcClient ipcClient,
        ResponseEnvelope startedResponse,
        CancellationToken cancellationToken)
    {
        var started = DeserializeData<TestRunStartedPayload>(startedResponse);
        if (started is null || string.IsNullOrWhiteSpace(started.runId))
        {
            return startedResponse;
        }

        int timeoutSeconds = original.TestTimeoutSeconds ?? ProtocolConstants.DefaultTestRunTimeoutSeconds;
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds + ProtocolConstants.TestRunCancelGraceSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var poll = await SendTestResultsPollAsync(
                target,
                ipcClient,
                started.runId,
                original.TestFailuresOnly,
                cancellationToken);
            if (!string.Equals(poll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
            {
                return poll;
            }

            poll = NormalizeTestResultEnvelope(CommandKind.TestRun, poll);
            if (!string.Equals(poll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
            {
                return poll;
            }

            var result = DeserializeData<TestRunResultPayload>(poll);
            if (result is null || !string.Equals(result.status, "Running", StringComparison.Ordinal))
            {
                return poll;
            }

            if (!original.JsonOutput)
            {
                Console.Error.WriteLine($"progress: {result.summary.completed}/{result.summary.total}");
            }
        }

        var finalPoll = await SendTestResultsPollAsync(
            target,
            ipcClient,
            started.runId,
            original.TestFailuresOnly,
            cancellationToken);
        finalPoll = NormalizeTestResultEnvelope(CommandKind.TestRun, finalPoll);
        if (!string.Equals(finalPoll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
        {
            return finalPoll;
        }

        var finalResult = DeserializeData<TestRunResultPayload>(finalPoll);
        if (finalResult is not null && string.Equals(finalResult.status, "Running", StringComparison.Ordinal))
        {
            return BuildTestResultFailureEnvelope(
                finalPoll,
                finalResult,
                ProtocolConstants.ErrorTestTimeout,
                string.IsNullOrWhiteSpace(finalResult.runId)
                    ? "Test run timed out."
                    : "Test run " + finalResult.runId + " timed out.");
        }

        return finalPoll;
    }

    private static async Task<ResponseEnvelope> SendTestResultsPollAsync(
        InstanceRecord target,
        LocalIpcClient ipcClient,
        string runId,
        bool failuresOnly,
        CancellationToken cancellationToken)
    {
        var command = new ParsedCommand(CommandKind.TestResults)
        {
            TestRunId = runId,
            TestFailuresOnly = failuresOnly,
        }.ToEnvelope();

        return await ipcClient.SendAsync(target, command, ProtocolConstants.DefaultLiveTimeoutMs, cancellationToken);
    }

    internal static async Task<ResponseEnvelope> PollRecordStatusAsync(
        ParsedCommand original,
        InstanceRecord target,
        Func<InstanceRecord, CommandEnvelope, int, CancellationToken, Task<ResponseEnvelope>> sendAsync,
        ResponseEnvelope startedResponse,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? pollInterval = null)
    {
        var started = DeserializeData<RecordStartedPayload>(startedResponse);
        if (started is null || string.IsNullOrWhiteSpace(started.recordingId))
        {
            return startedResponse;
        }

        delayAsync ??= Task.Delay;
        TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(1);
        int durationSeconds = original.RecordDuration ?? ProtocolConstants.MaxRecordDurationSeconds;
        DateTime deadline = DateTime.UtcNow.AddSeconds(durationSeconds + 15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await delayAsync(interval, cancellationToken);

            var poll = await SendRecordStatusPollAsync(target, sendAsync, started.recordingId, cancellationToken);
            if (!string.Equals(poll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
            {
                return poll;
            }

            poll = NormalizeRecordResultEnvelope(CommandKind.RecordStatus, poll);
            if (!string.Equals(poll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
            {
                return poll;
            }

            var result = DeserializeData<RecordResultPayload>(poll);
            if (result is null || !string.Equals(result.status, "Recording", StringComparison.Ordinal))
            {
                return poll;
            }
        }

        var finalPoll = await SendRecordStatusPollAsync(target, sendAsync, started.recordingId, cancellationToken);
        if (!string.Equals(finalPoll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
        {
            return finalPoll;
        }

        finalPoll = NormalizeRecordResultEnvelope(CommandKind.RecordStatus, finalPoll);
        if (!string.Equals(finalPoll.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
        {
            return finalPoll;
        }

        var finalResult = DeserializeData<RecordResultPayload>(finalPoll);
        if (finalResult is not null && string.Equals(finalResult.status, "Recording", StringComparison.Ordinal))
        {
            return ResponseEnvelope.Failure(
                finalPoll.requestId,
                finalPoll.target,
                ProtocolConstants.ErrorRecordTimeout,
                "Recording " + started.recordingId + " did not finalize before the wait timeout.",
                retryable: false,
                finalPoll.durationMs,
                finalPoll.transport,
                finalPoll.data?.GetRawText());
        }

        return finalPoll;
    }

    private static async Task<ResponseEnvelope> SendRecordStatusPollAsync(
        InstanceRecord target,
        Func<InstanceRecord, CommandEnvelope, int, CancellationToken, Task<ResponseEnvelope>> sendAsync,
        string recordingId,
        CancellationToken cancellationToken)
    {
        var command = new ParsedCommand(CommandKind.RecordStatus)
        {
            RecordRunId = recordingId,
        }.ToEnvelope();

        return await sendAsync(target, command, ProtocolConstants.DefaultLiveTimeoutMs, cancellationToken);
    }

    internal static async Task<ResponseEnvelope> PollEditorReadyAsync(
        ParsedCommand original,
        ResponseEnvelope startedResponse,
        Func<CommandEnvelope, int, CancellationToken, Task<ResponseEnvelope>> sendAsync,
        CancellationToken cancellationToken = default,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        delayAsync ??= Task.Delay;
        TimeSpan waitTimeout = timeout ?? TimeSpan.FromSeconds(ProtocolConstants.DefaultCompileRefreshWaitTimeoutSeconds);
        TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(2);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int maxConsecutiveTransportFailures = 6;
        int consecutiveTransportFailures = 0;
        bool hadPermanentTransportFailure = false;

        while (stopwatch.Elapsed < waitTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remainingBeforeDelay = waitTimeout - stopwatch.Elapsed;
            TimeSpan delay = remainingBeforeDelay < interval ? remainingBeforeDelay : interval;
            if (delay > TimeSpan.Zero)
            {
                await delayAsync(delay, cancellationToken);
            }

            TimeSpan remaining = waitTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            try
            {
                using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                pollCts.CancelAfter(remaining);
                int timeoutMs = Math.Max(1, (int)Math.Min(ProtocolConstants.DefaultLiveTimeoutMs, remaining.TotalMilliseconds));
                var poll = await sendAsync(BuildStatusCommand(), timeoutMs, pollCts.Token);

                switch (GetStatusPollResult(poll))
                {
                    case StatusPollResult.Ready:
                        return BuildEditorReadyResponse(startedResponse, stopwatch.ElapsedMilliseconds);
                    case StatusPollResult.StillBusy:
                        consecutiveTransportFailures = 0;
                        hadPermanentTransportFailure = false;
                        break;
                    case StatusPollResult.InvalidStatusPayload:
                        return BuildInvalidStatusPayloadResponse(startedResponse, stopwatch.ElapsedMilliseconds);
                    case StatusPollResult.TransientFailure:
                        consecutiveTransportFailures++;
                        if (consecutiveTransportFailures >= maxConsecutiveTransportFailures)
                        {
                            return BuildStatusUnavailableResponse(
                                startedResponse,
                                stopwatch.ElapsedMilliseconds,
                                "Unity status polling did not receive a valid ready/busy response after "
                                    + consecutiveTransportFailures
                                    + " consecutive transport failures.");
                        }

                        break;
                    case StatusPollResult.NonRetryableFailure:
                        return poll;
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                break;
            }
            catch (Exception ex)
            {
                if (IsPermanentTransportException(ex))
                {
                    if (hadPermanentTransportFailure)
                    {
                        return BuildStatusUnavailableResponse(startedResponse, stopwatch.ElapsedMilliseconds, ex.Message);
                    }

                    hadPermanentTransportFailure = true;
                    continue;
                }

                consecutiveTransportFailures++;
                if (consecutiveTransportFailures >= maxConsecutiveTransportFailures)
                {
                    return BuildStatusUnavailableResponse(startedResponse, stopwatch.ElapsedMilliseconds, ex.Message);
                }
            }
        }

        return BuildEditorReadyTimeoutResponse(original, startedResponse, stopwatch.ElapsedMilliseconds, waitTimeout);
    }

    private static CommandEnvelope BuildStatusCommand()
    {
        return new CommandEnvelope
        {
            requestId = Guid.NewGuid().ToString("N"),
            command = ProtocolConstants.CommandStatus,
            argumentsJson = "{}",
        };
    }

    private enum StatusPollResult
    {
        Ready,
        StillBusy,
        InvalidStatusPayload,
        TransientFailure,
        NonRetryableFailure,
    }

    private static StatusPollResult GetStatusPollResult(ResponseEnvelope response)
    {
        if (!string.Equals(response.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
        {
            return IsTransientStatusPollFailure(response)
                ? StatusPollResult.TransientFailure
                : StatusPollResult.NonRetryableFailure;
        }

        StatusPayload? status;
        try
        {
            status = DeserializeData<StatusPayload>(response);
        }
        catch (JsonException)
        {
            return StatusPollResult.InvalidStatusPayload;
        }

        if (status is null)
        {
            return StatusPollResult.InvalidStatusPayload;
        }

        return status.isCompiling || status.isUpdating
            ? StatusPollResult.StillBusy
            : StatusPollResult.Ready;
    }

    private static bool IsTransientStatusPollFailure(ResponseEnvelope response)
    {
        string? code = response.error?.code;
        return string.Equals(code, "LIVE_UNAVAILABLE", StringComparison.Ordinal)
            || response.retryable;
    }

    private static bool IsPermanentTransportException(Exception ex)
    {
        return ex is FileNotFoundException
            or DirectoryNotFoundException
            || (ex is System.Net.Sockets.SocketException socketException && socketException.NativeErrorCode == 2);
    }

    private static ResponseEnvelope BuildEditorReadyResponse(ResponseEnvelope startedResponse, long waitedMs)
    {
        JsonObject data = CloneDataObject(startedResponse.data);
        data["ready"] = true;
        data["readyMessage"] = "ready";
        data["waitedMs"] = waitedMs;

        return ResponseEnvelope.Success(
            startedResponse.requestId,
            startedResponse.target,
            CreateDataElement(data),
            startedResponse.durationMs + waitedMs,
            startedResponse.transport);
    }

    private static JsonObject CloneDataObject(JsonElement? data)
    {
        if (!data.HasValue)
        {
            return new JsonObject();
        }

        JsonElement element = data.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            string? json = element.GetString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject();
            }

            var node = JsonNode.Parse(json);
            return node as JsonObject ?? new JsonObject();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new JsonObject();
        }

        var clone = JsonNode.Parse(element.GetRawText());
        return clone as JsonObject ?? new JsonObject();
    }

    private static ResponseEnvelope BuildEditorReadyTimeoutResponse(
        ParsedCommand original,
        ResponseEnvelope startedResponse,
        long waitedMs,
        TimeSpan waitTimeout)
    {
        string code = original.Kind == CommandKind.Compile
            ? ProtocolConstants.ErrorCompileWaitTimeout
            : ProtocolConstants.ErrorRefreshWaitTimeout;
        string command = original.Kind == CommandKind.Compile ? "compile" : "refresh";
        string timeoutSeconds = FormatSeconds(waitTimeout);

        var details = new
        {
            command,
            waitedMs,
            timeoutSeconds = waitTimeout.TotalSeconds,
        };

        return ResponseEnvelope.Failure(
            startedResponse.requestId,
            startedResponse.target,
            code,
            command + " --wait timed out after "
                + timeoutSeconds
                + " seconds waiting for the Editor to finish compiling/importing and become reachable.",
            retryable: true,
            durationMs: waitedMs,
            transport: "cli",
            details: ProtocolJson.Serialize(details));
    }

    private static ResponseEnvelope BuildInvalidStatusPayloadResponse(ResponseEnvelope startedResponse, long waitedMs)
    {
        return BuildStatusUnavailableResponse(
            startedResponse,
            waitedMs,
            "Unity status returned success but did not include a valid status payload.");
    }

    private static ResponseEnvelope BuildStatusUnavailableResponse(
        ResponseEnvelope startedResponse,
        long waitedMs,
        string details)
    {
        return ResponseEnvelope.Failure(
            startedResponse.requestId,
            startedResponse.target,
            "LIVE_UNAVAILABLE",
            "Unity Editor가 실행 중이지 않거나 Bridge가 활성화되지 않았습니다.",
            retryable: true,
            durationMs: waitedMs,
            transport: "cli",
            details: details);
    }

    private static string FormatSeconds(TimeSpan value)
    {
        double totalSeconds = value.TotalSeconds;
        return Math.Abs(totalSeconds - Math.Round(totalSeconds)) < 0.001
            ? ((int)Math.Round(totalSeconds)).ToString()
            : totalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static T? DeserializeData<T>(ResponseEnvelope response)
    {
        if (!response.data.HasValue)
        {
            return default;
        }

        JsonElement data = response.data.Value;
        if (data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        if (data.ValueKind == JsonValueKind.String)
        {
            string? json = data.GetString();
            return string.IsNullOrWhiteSpace(json)
                ? default
                : ProtocolJson.Deserialize<T>(json);
        }

        return JsonSerializer.Deserialize<T>(data.GetRawText(), ProtocolJson.Default);
    }

    private static ResponseEnvelope NormalizeResultEnvelope(CommandKind kind, ResponseEnvelope response)
    {
        response = NormalizeTestResultEnvelope(kind, response);
        return NormalizeRecordResultEnvelope(kind, response);
    }

    internal static ResponseEnvelope NormalizeTestResultEnvelope(CommandKind kind, ResponseEnvelope response)
    {
        if (kind != CommandKind.TestRun)
        {
            return response;
        }

        if (!string.Equals(response.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
        {
            return response;
        }

        var result = DeserializeData<TestRunResultPayload>(response);
        if (result is null || !ProtocolHelpers.IsTestRunResultStatusError(result.status))
        {
            return response;
        }

        return BuildTestResultFailureEnvelope(
            response,
            result,
            ProtocolHelpers.GetTestRunResultErrorCode(result.status, result.warnings),
            ProtocolHelpers.BuildTestRunResultErrorMessage(result));
    }

    internal static ResponseEnvelope NormalizeRecordResultEnvelope(CommandKind kind, ResponseEnvelope response)
    {
        if (kind is not (CommandKind.RecordStatus or CommandKind.RecordStop))
        {
            return response;
        }

        if (!string.Equals(response.status, ProtocolConstants.StatusSuccess, StringComparison.Ordinal))
        {
            return response;
        }

        var result = DeserializeData<RecordResultPayload>(response);
        if (result is null
            || string.Equals(result.status, "Recording", StringComparison.Ordinal)
            || string.Equals(result.status, "Completed", StringComparison.Ordinal))
        {
            return response;
        }

        return BuildRecordResultFailureEnvelope(
            response,
            result,
            GetRecordResultErrorCode(result.status),
            BuildRecordResultErrorMessage(result));
    }

    private static ResponseEnvelope BuildTestResultFailureEnvelope(
        ResponseEnvelope source,
        TestRunResultPayload result,
        string errorCode,
        string message)
    {
        return ResponseEnvelope.Failure(
            source.requestId,
            source.target,
            errorCode,
            message,
            retryable: false,
            source.durationMs,
            source.transport,
            GetDataDetailsJson(source, result));
    }

    private static string GetDataDetailsJson(ResponseEnvelope source, TestRunResultPayload fallback)
    {
        if (!source.data.HasValue)
        {
            return ProtocolJson.Serialize(fallback);
        }

        JsonElement data = source.data.Value;
        if (data.ValueKind == JsonValueKind.String)
        {
            string? json = data.GetString();
            return string.IsNullOrWhiteSpace(json)
                ? ProtocolJson.Serialize(fallback)
                : json;
        }

        return data.GetRawText();
    }

    private static ResponseEnvelope BuildRecordResultFailureEnvelope(
        ResponseEnvelope source,
        RecordResultPayload result,
        string errorCode,
        string message)
    {
        return ResponseEnvelope.Failure(
            source.requestId,
            source.target,
            errorCode,
            message,
            retryable: false,
            source.durationMs,
            source.transport,
            GetRecordDataDetailsJson(source, result));
    }

    private static string GetRecordResultErrorCode(string? status)
    {
        return status switch
        {
            "Interrupted" => ProtocolConstants.ErrorRecordInterrupted,
            "NotFound" => ProtocolConstants.ErrorRecordNotFound,
            "Failed" => ProtocolConstants.ErrorRecordFailed,
            _ => ProtocolConstants.ErrorRecordFailed,
        };
    }

    private static string BuildRecordResultErrorMessage(RecordResultPayload result)
    {
        string id = string.IsNullOrWhiteSpace(result.recordingId) ? "recording" : "Recording " + result.recordingId;
        return result.status switch
        {
            "Interrupted" => id + " was interrupted before it completed.",
            "NotFound" => id + " was not found.",
            "Failed" => id + " failed to finalize.",
            _ => id + " ended with status " + result.status + ".",
        };
    }

    private static string GetRecordDataDetailsJson(ResponseEnvelope source, RecordResultPayload fallback)
    {
        if (!source.data.HasValue)
        {
            return ProtocolJson.Serialize(fallback);
        }

        JsonElement data = source.data.Value;
        if (data.ValueKind == JsonValueKind.String)
        {
            string? json = data.GetString();
            return string.IsNullOrWhiteSpace(json)
                ? ProtocolJson.Serialize(fallback)
                : json;
        }

        return data.GetRawText();
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

    internal static InstanceRecord? ResolveTarget(InstanceRegistry registry, string? projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var canonicalProjectRoot = ProtocolConstants.GetCanonicalPath(projectRoot);
            var projectHash = ProtocolConstants.ComputeProjectHash(canonicalProjectRoot);
            var match = registry.instances.FirstOrDefault(item =>
                string.Equals(item.projectRoot, canonicalProjectRoot, StringComparison.OrdinalIgnoreCase));
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

        var liveInstances = registry.instances
            .Where(item => !string.Equals(item.state, "offline", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (registry.activeProjectRootPinned
            && !string.IsNullOrWhiteSpace(registry.activeProjectRoot))
        {
            var pinned = liveInstances.FirstOrDefault(item =>
                string.Equals(item.projectRoot, registry.activeProjectRoot, StringComparison.OrdinalIgnoreCase));
            if (pinned is not null)
            {
                return pinned;
            }
        }

        if (liveInstances.Length == 1)
        {
            return liveInstances[0];
        }

        if (liveInstances.Length >= 2)
        {
            throw CreateAmbiguousTargetException(liveInstances);
        }

        return null;
    }

    private static CliUsageException CreateAmbiguousTargetException(InstanceRecord[] candidates)
    {
        var lines = candidates
            .OrderBy(item => item.projectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.projectRoot, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"  - {item.projectName} ({item.state})  {item.projectRoot}");
        return new CliUsageException(
            "실행 중인 Unity 인스턴스가 여러 개여서 대상을 결정할 수 없습니다. "
            + "--project로 지정하거나 `unity-cli instances use <projectPath|projectName>`로 기본 대상을 고정하세요.\n후보:\n"
            + string.Join("\n", lines));
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
                Console.Error.WriteLine(details.ValueKind == JsonValueKind.String
                    ? details.GetString()
                    : JsonSerializer.Serialize(details, ProtocolJson.Default));
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
