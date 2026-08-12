using System.Diagnostics;
using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

/// <summary>
/// Local `editor launch` flow: pre-flight double-launch detection, editor spawn,
/// and bridge-readiness polling. Never talks IPC itself — readiness is observed
/// through the shared instance registry, which the bridge writes only after the
/// listener and token sidecar are ready.
/// </summary>
public static class EditorLauncher
{
    private const int DefaultLaunchWaitTimeoutSeconds = 300;
    private const int PollIntervalMilliseconds = 2000;

    public static async Task<ResponseEnvelope> LaunchAsync(
        ParsedCommand parsed,
        InstanceRegistryStore registryStore,
        string? projectRoot)
    {
        var stopwatch = Stopwatch.StartNew();
        string requestId = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new CliUsageException("editor launch에는 대상 프로젝트가 필요합니다. --project <path>를 지정하세요.");
        }

        string canonicalRoot = ProtocolConstants.GetCanonicalPath(projectRoot);
        if (!File.Exists(Path.Combine(canonicalRoot, "ProjectSettings", "ProjectVersion.txt")))
        {
            return Failure(
                requestId,
                ProtocolConstants.ErrorEditorLaunchFailed,
                "Unity 프로젝트가 아닙니다 (ProjectSettings/ProjectVersion.txt 없음): " + canonicalRoot,
                retryable: false,
                stopwatch);
        }

        // Pre-flight ①: live registry match → idempotent reuse.
        InstanceRecord? live = FindLiveInstance(registryStore, canonicalRoot);
        if (live != null)
        {
            string requestedMode = RequestedModeLabel(parsed);
            string note = string.IsNullOrEmpty(live.editorMode) || string.Equals(live.editorMode, requestedMode, StringComparison.Ordinal)
                ? string.Empty
                : $"요청 모드({requestedMode})와 실행 중인 에디터 모드({live.editorMode})가 다릅니다. 모드 전환이 필요하면 editor stop 후 다시 launch 하세요.";

            return Success(requestId, new
            {
                launched = false,
                reused = true,
                pid = live.editorProcessId,
                mode = string.IsNullOrEmpty(live.editorMode) ? "unknown" : live.editorMode,
                unityVersion = live.unityVersion,
                projectRoot = canonicalRoot,
                waitedMs = 0L,
                note,
            }, stopwatch);
        }

        // Pre-flight ②: a main-editor process on this project that never registered
        // (bridge package missing, or still booting). Launching again would hit Unity's
        // own lock — batch fails fast, GUI blocks on a modal. Refuse with context.
        int? strayPid = FindStrayEditorProcessId(canonicalRoot);
        if (strayPid.HasValue)
        {
            return Failure(
                requestId,
                ProtocolConstants.ErrorEditorAlreadyRunning,
                $"이 프로젝트를 연 에디터 프로세스(PID {strayPid.Value})가 이미 있지만 브릿지 인스턴스로 등록되어 있지 않습니다. "
                + "부팅/컴파일 중이면 잠시 후 다시 시도하고, 패키지 미설치 프로젝트면 해당 에디터를 직접 사용하세요.",
                retryable: true,
                stopwatch);
        }

        string? editorBinary = !string.IsNullOrWhiteSpace(parsed.EditorPathOverride)
            ? (File.Exists(parsed.EditorPathOverride) ? parsed.EditorPathOverride : null)
            : UnityEditorLocator.TryResolve(canonicalRoot);
        if (editorBinary == null)
        {
            return Failure(
                requestId,
                ProtocolConstants.ErrorEditorLaunchFailed,
                "프로젝트 버전에 맞는 Unity 에디터 바이너리를 찾지 못했습니다. Unity Hub로 해당 버전을 설치하거나 --editor-path로 지정하세요.",
                retryable: false,
                stopwatch);
        }

        string logDirectory = Path.Combine(canonicalRoot, "Library", "com.yhc509.unity-cli-bridge");
        Directory.CreateDirectory(logDirectory);
        string logFile = Path.Combine(logDirectory, "editor-launch.log");

        ProcessStartInfo startInfo = BuildStartInfo(editorBinary, BuildLaunchArguments(parsed, canonicalRoot, logFile));

        Process editorProcess;
        try
        {
            editorProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start가 null을 반환했습니다.");
        }
        catch (Exception exception)
        {
            return Failure(
                requestId,
                ProtocolConstants.ErrorEditorLaunchFailed,
                "에디터 기동에 실패했습니다: " + exception.Message,
                retryable: false,
                stopwatch);
        }

        if (parsed.EditorNoWait)
        {
            return Success(requestId, new
            {
                launched = true,
                reused = false,
                pid = editorProcess.Id,
                mode = RequestedModeLabel(parsed),
                unityVersion = string.Empty,
                projectRoot = canonicalRoot,
                waitedMs = 0L,
                note = "--no-wait: 브릿지 준비를 기다리지 않았습니다. editor-launch.log와 instances list로 상태를 확인하세요.",
            }, stopwatch);
        }

        int timeoutSeconds = parsed.EditorWaitTimeoutSeconds ?? DefaultLaunchWaitTimeoutSeconds;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (editorProcess.HasExited)
            {
                return Failure(
                    requestId,
                    ProtocolConstants.ErrorEditorLaunchFailed,
                    $"에디터 프로세스가 준비 전에 종료되었습니다 (exit {editorProcess.ExitCode}). 로그 확인: {logFile}",
                    retryable: false,
                    stopwatch);
            }

            InstanceRecord? ready = FindLiveInstance(registryStore, canonicalRoot);
            if (ready != null)
            {
                return Success(requestId, new
                {
                    launched = true,
                    reused = false,
                    pid = ready.editorProcessId,
                    mode = string.IsNullOrEmpty(ready.editorMode) ? RequestedModeLabel(parsed) : ready.editorMode,
                    unityVersion = ready.unityVersion,
                    projectRoot = canonicalRoot,
                    waitedMs = stopwatch.ElapsedMilliseconds,
                    note = string.Empty,
                }, stopwatch);
            }

            await Task.Delay(PollIntervalMilliseconds);
        }

        return Failure(
            requestId,
            ProtocolConstants.ErrorEditorWaitTimeout,
            $"에디터(PID {editorProcess.Id})는 떠 있지만 {timeoutSeconds}초 안에 브릿지가 준비되지 않았습니다. "
            + $"첫 임포트가 긴 프로젝트면 --timeout을 늘리세요. 로그: {logFile}",
            retryable: true,
            stopwatch);
    }

    /// <summary>
    /// stdio를 분리해 에디터를 낳는다. 그냥 상속시키면 스폰된 에디터가 CLI의
    /// stdout/stderr 파이프 write-end를 쥐고 있어, `unity-cli editor launch | grep …`
    /// 같은 파이프라인이 CLI 종료 후에도 EOF를 못 받고 영원히 매달린다 (실측).
    /// Unix에서는 `sh -c 'exec …'`로 감싸 /dev/null에 연결한다 — exec이 셸을
    /// 대체하므로 Process.Id는 그대로 에디터 PID다. 에디터 출력은 -logFile이 담당.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string editorBinary, string[] arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            var windowsStartInfo = new ProcessStartInfo
            {
                FileName = editorBinary,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                windowsStartInfo.ArgumentList.Add(argument);
            }

            return windowsStartInfo;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("exec \"$0\" \"$@\" </dev/null >/dev/null 2>&1");
        startInfo.ArgumentList.Add(editorBinary);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static string[] BuildLaunchArguments(ParsedCommand parsed, string projectRoot, string logFile)
    {
        var arguments = new List<string> { "-projectPath", projectRoot, "-logFile", logFile };
        if (!parsed.EditorGui)
        {
            arguments.Add("-batchmode");
        }

        if (parsed.EditorNoGraphics)
        {
            if (!arguments.Contains("-batchmode"))
            {
                arguments.Add("-batchmode");
            }

            arguments.Add("-nographics");
        }

        return arguments.ToArray();
    }

    public static string RequestedModeLabel(ParsedCommand parsed)
    {
        if (parsed.EditorNoGraphics)
        {
            return "headless-nographics";
        }

        return parsed.EditorGui ? "gui" : "headless";
    }

    /// <summary>
    /// ps 한 줄이 "이 프로젝트를 연 메인 에디터 프로세스"인지 판별한다.
    /// 오탐 두 종류를 걸러낸다 (실측 근거): -adb2 AssetImportWorker는 같은
    /// -projectPath를 갖고, Unity Hub Helper는 argv에 최근 프로젝트 경로 목록을 담는다.
    /// </summary>
    public static bool IsMainEditorProcessLine(string psLine, string projectRoot)
    {
        if (psLine.Contains("-adb2", StringComparison.OrdinalIgnoreCase)
            || psLine.Contains("Unity Hub", StringComparison.Ordinal))
        {
            return false;
        }

        int flagIndex = psLine.IndexOf("-projectpath", StringComparison.OrdinalIgnoreCase);
        if (flagIndex < 0)
        {
            return false;
        }

        string afterFlag = psLine[(flagIndex + "-projectpath".Length)..].TrimStart();
        return afterFlag.StartsWith(projectRoot, StringComparison.Ordinal)
            && (afterFlag.Length == projectRoot.Length
                || afterFlag[projectRoot.Length] == ' ');
    }

    private static InstanceRecord? FindLiveInstance(InstanceRegistryStore registryStore, string canonicalRoot)
    {
        InstanceRegistry registry = registryStore.Load();
        foreach (InstanceRecord record in registry.instances ?? Array.Empty<InstanceRecord>())
        {
            if (!string.Equals(record.projectRoot, canonicalRoot, StringComparison.Ordinal))
            {
                continue;
            }

            if (record.editorProcessId <= 0 || !IsProcessAlive(record.editorProcessId))
            {
                continue;
            }

            return record;
        }

        return null;
    }

    private static int? FindStrayEditorProcessId(string canonicalRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            // Command-line inspection needs Win32 APIs; registry + Unity's own lock
            // cover the double-launch case there. (Live-verified surface is macOS.)
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/ps",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            startInfo.ArgumentList.Add("-axo");
            startInfo.ArgumentList.Add("pid=,command=");

            using Process? ps = Process.Start(startInfo);
            if (ps == null)
            {
                return null;
            }

            string output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(5000);
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || !IsMainEditorProcessLine(trimmed, canonicalRoot))
                {
                    continue;
                }

                int spaceIndex = trimmed.IndexOf(' ');
                if (spaceIndex > 0 && int.TryParse(trimmed[..spaceIndex], out int pid))
                {
                    return pid;
                }
            }
        }
        catch (Exception)
        {
            // Pre-flight scan is best-effort; Unity's own project lock is the backstop.
        }

        return null;
    }

    internal static bool IsProcessAlive(int pid)
    {
        try
        {
            Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static ResponseEnvelope Success(string requestId, object payload, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return ResponseEnvelope.Success(
            requestId,
            null,
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            stopwatch.ElapsedMilliseconds,
            "cli");
    }

    private static ResponseEnvelope Failure(
        string requestId,
        string errorCode,
        string message,
        bool retryable,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return ResponseEnvelope.Failure(
            requestId,
            null,
            errorCode,
            message,
            retryable: retryable,
            durationMs: stopwatch.ElapsedMilliseconds,
            transport: "cli");
    }
}
