#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class TestCommandHandler
    {
        private static readonly object _activeLock = new object();
        private static bool _hasActiveRun;
        private const int TestListTimeoutSeconds = 30;
        private const int TestFilterResolutionTimeoutSeconds = 15;
        private const string NoMatchingTestName = "\u0000unity-cli-bridge-no-matching-test\u0000";

        public bool CanHandle(string command)
        {
            return ProtocolHelpers.IsTestCommand(command);
        }

        public bool IsDeferred(string command, string? argumentsJson = null)
        {
            return ProtocolHelpers.IsDeferredTestCommand(command);
        }

        public string Handle(string command, string argumentsJson)
        {
            if (string.Equals(command, ProtocolConstants.CommandTestList, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Deferred test command must be started through StartDeferred: " + command);
            }

            if (string.Equals(command, ProtocolConstants.CommandTestResults, StringComparison.Ordinal))
            {
                return HandleResults(argumentsJson);
            }

            throw new InvalidOperationException("Deferred test command must be started through StartDeferred: " + command);
        }

        public void StartDeferred(
            string command,
            string argumentsJson,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash)
        {
            if (completion.Task.IsCompleted)
            {
                return;
            }

            string requestId = GetRequestId(completion);
            if (string.Equals(command, ProtocolConstants.CommandTestList, StringComparison.Ordinal))
            {
                StartDeferredList(argumentsJson, completion, projectHash, requestId);
                return;
            }

            if (!string.Equals(command, ProtocolConstants.CommandTestRun, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsupported deferred test command: " + command);
            }

            if (!TryBeginRun_Internal(out string activeRunId, out string activeMode))
            {
                completion.TrySetResult(ResponseEnvelope.Failure(
                    requestId,
                    projectHash,
                    ProtocolConstants.ErrorTestBusy,
                    "이미 실행 중인 테스트 run이 있습니다 (runId=" + activeRunId + ", mode=" + activeMode + ").",
                    true,
                    0,
                    ProtocolConstants.TransportLive,
                    null));
                return;
            }

            try
            {
                TestRunArgs args = ProtocolJson.Deserialize<TestRunArgs>(argumentsJson) ?? new TestRunArgs();
                if (string.Equals(args.mode, "edit", StringComparison.Ordinal))
                {
                    StartEditModeRun(args, completion, projectHash, requestId);
                    return;
                }

                if (string.Equals(args.mode, "play", StringComparison.Ordinal))
                {
                    StartPlayModeRun(args, completion, projectHash, requestId);
                    return;
                }

                EndRun();
                completion.TrySetResult(ResponseEnvelope.Failure(
                    requestId,
                    projectHash,
                    ProtocolConstants.ErrorTestInvalidMode,
                    "test run --mode는 edit, play 중 하나여야 합니다: '" + args.mode + "'",
                    false,
                    0,
                    ProtocolConstants.TransportLive,
                    null));
            }
            catch
            {
                EndRun();
                throw;
            }
        }

        private static void StartDeferredList(
            string argumentsJson,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId)
        {
            TestListArgs args = ProtocolJson.Deserialize<TestListArgs>(argumentsJson) ?? new TestListArgs();
            bool includeEditMode = string.Equals(args.mode, "edit", StringComparison.Ordinal)
                || string.Equals(args.mode, "all", StringComparison.Ordinal);
            bool includePlayMode = string.Equals(args.mode, "play", StringComparison.Ordinal)
                || string.Equals(args.mode, "all", StringComparison.Ordinal);

            if (!includeEditMode && !includePlayMode)
            {
                completion.TrySetResult(ResponseEnvelope.Failure(
                    requestId,
                    projectHash,
                    ProtocolConstants.ErrorTestInvalidMode,
                    "test list --mode는 edit, play, all 중 하나여야 합니다: '" + args.mode + "'",
                    false,
                    0,
                    ProtocolConstants.TransportLive,
                    null));
                return;
            }

            var editEntries = new List<TestListEntry>();
            var playEntries = new List<TestListEntry>();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var stopwatch = Stopwatch.StartNew();
            int expectedCallbacks = (includeEditMode ? 1 : 0) + (includePlayMode ? 1 : 0);
            int receivedCallbacks = 0;
            bool completed = false;
            bool pollRegistered = false;

            void Cleanup()
            {
                if (pollRegistered)
                {
                    EditorApplication.update -= Poll;
                    pollRegistered = false;
                }

                UnityEngine.Object.DestroyImmediate(api);
            }

            void Complete(ResponseEnvelope response)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                Cleanup();
                completion.TrySetResult(response);
            }

            void CompleteSuccess()
            {
                if (completed)
                {
                    return;
                }

                stopwatch.Stop();
                var tests = new List<TestListEntry>();
                if (includeEditMode)
                {
                    tests.AddRange(editEntries);
                }

                if (includePlayMode)
                {
                    tests.AddRange(playEntries);
                }

                Complete(ResponseEnvelope.Success(
                    requestId,
                    projectHash,
                    ProtocolJson.Serialize(new TestListPayload
                    {
                        mode = args.mode,
                        tests = tests.ToArray(),
                    }),
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive));
            }

            void CompleteTimeout()
            {
                if (completed)
                {
                    return;
                }

                stopwatch.Stop();
                Complete(ResponseEnvelope.Failure(
                    requestId,
                    projectHash,
                    ProtocolConstants.ErrorTestListTimeout,
                    "test list가 " + TestListTimeoutSeconds + "초 내 완료되지 않았습니다.",
                    false,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    null));
            }

            void Poll()
            {
                if (completion.Task.IsCompleted)
                {
                    if (!completed)
                    {
                        completed = true;
                        Cleanup();
                    }

                    return;
                }

                if (receivedCallbacks >= expectedCallbacks)
                {
                    CompleteSuccess();
                    return;
                }

                if (stopwatch.Elapsed.TotalSeconds > TestListTimeoutSeconds)
                {
                    CompleteTimeout();
                }
            }

            void RequestList(TestMode mode, string modeLabel, List<TestListEntry> targetEntries)
            {
                api.RetrieveTestList(mode, adaptor =>
                {
                    if (completed)
                    {
                        return;
                    }

                    AppendListForMode(adaptor, modeLabel, targetEntries);
                    receivedCallbacks++;
                });
            }

            try
            {
                EditorApplication.update += Poll;
                pollRegistered = true;

                if (includeEditMode)
                {
                    RequestList(TestMode.EditMode, "edit", editEntries);
                }

                if (includePlayMode)
                {
                    RequestList(TestMode.PlayMode, "play", playEntries);
                }

                Poll();
            }
            catch
            {
                if (!completed)
                {
                    completed = true;
                    Cleanup();
                }

                throw;
            }
        }

        private static void AppendListForMode(
            ITestAdaptor? root,
            string modeLabel,
            List<TestListEntry> entries)
        {
            if (root == null)
            {
                return;
            }

            CollectLeafTests(root, modeLabel, entries);
        }

        private static void CollectLeafTests(
            ITestAdaptor adaptor,
            string modeLabel,
            List<TestListEntry> entries)
        {
            if (adaptor.IsSuite)
            {
                foreach (ITestAdaptor child in adaptor.Children)
                {
                    CollectLeafTests(child, modeLabel, entries);
                }

                return;
            }

            entries.Add(new TestListEntry
            {
                fullName = adaptor.FullName ?? string.Empty,
                assembly = GetAssemblyName(adaptor),
                mode = modeLabel,
                categories = adaptor.Categories ?? Array.Empty<string>(),
            });
        }

        private void ResolveFullNamesAsync(
            TestMode mode,
            string substringFilter,
            Action<string[]> onResolved,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(substringFilter))
            {
                onResolved(Array.Empty<string>());
                return;
            }

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            ITestAdaptor? root = null;
            bool completed = false;
            bool pollRegistered = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(TestFilterResolutionTimeoutSeconds);

            void Cleanup()
            {
                if (pollRegistered)
                {
                    EditorApplication.update -= Poll;
                    pollRegistered = false;
                }

                UnityEngine.Object.DestroyImmediate(api);
            }

            void CompleteResolved(string[] resolvedFullNames)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                Cleanup();
                onResolved(resolvedFullNames);
            }

            void CompleteError(string message)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                Cleanup();
                onError(message);
            }

            void Poll()
            {
                if (root != null)
                {
                    var matched = new List<string>();
                    CollectMatchingFullNames(root, substringFilter, matched);
                    CompleteResolved(matched.ToArray());
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    CompleteError(
                        "Filter substring resolution timeout ("
                        + TestFilterResolutionTimeoutSeconds
                        + "s).");
                }
            }

            try
            {
                api.RetrieveTestList(mode, adaptor => root = adaptor);
                EditorApplication.update += Poll;
                pollRegistered = true;
                Poll();
            }
            catch (Exception exception)
            {
                CompleteError("Filter substring resolution failed: " + exception.Message);
            }
        }

        internal static void CollectMatchingFullNames(
            ITestAdaptor adaptor,
            string needle,
            List<string> matched)
        {
            if (adaptor.IsSuite)
            {
                foreach (ITestAdaptor child in adaptor.Children)
                {
                    CollectMatchingFullNames(child, needle, matched);
                }

                return;
            }

            string fullName = adaptor.FullName ?? string.Empty;
            if (ProtocolHelpers.TestFullNameMatchesFilter(fullName, needle))
            {
                matched.Add(fullName);
            }
        }

        private void ResolveFilterFullNamesThenStart(
            TestMode mode,
            TestRunArgs args,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId,
            Stopwatch stopwatch,
            Action<string[]?> start)
        {
            if (string.IsNullOrEmpty(args.filter))
            {
                TryStartResolvedRun(null);
                return;
            }

            ResolveFullNamesAsync(
                mode,
                args.filter,
                resolvedFullNames => TryStartResolvedRun(resolvedFullNames),
                errorMessage =>
                {
                    stopwatch.Stop();
                    EndRun();
                    completion.TrySetResult(ResponseEnvelope.Failure(
                        requestId,
                        projectHash,
                        ProtocolConstants.ErrorTestListTimeout,
                        errorMessage,
                        false,
                        stopwatch.ElapsedMilliseconds,
                        ProtocolConstants.TransportLive,
                        null));
                });

            void TryStartResolvedRun(string[]? resolvedFullNames)
            {
                if (completion.Task.IsCompleted)
                {
                    stopwatch.Stop();
                    EndRun();
                    return;
                }

                try
                {
                    start(resolvedFullNames);
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    EndRun();
                    completion.TrySetResult(ResponseEnvelope.Failure(
                        requestId,
                        projectHash,
                        "TEST_RUN_START_FAILED",
                        "test run을 시작할 수 없습니다: " + exception.Message,
                        false,
                        stopwatch.ElapsedMilliseconds,
                        ProtocolConstants.TransportLive,
                        null));
                }
            }
        }

        private static string HandleResults(string argumentsJson)
        {
            TestResultsArgs args = ProtocolJson.Deserialize<TestResultsArgs>(argumentsJson) ?? new TestResultsArgs();
            string runId = args.runId;
            if (string.IsNullOrEmpty(runId))
            {
                runId = TryReadLastRunId();
                if (string.IsNullOrEmpty(runId))
                {
                    throw new CommandFailureException(
                        ProtocolConstants.ErrorTestRunNotFound,
                        "조회할 test run이 없습니다 (last-run.json 미존재).");
                }
            }

            string projectRoot = Path.Combine(Application.dataPath, "..");
            string runsDir = Path.Combine(projectRoot, ProtocolConstants.TestRunsDirectoryRelative);
            string filePath = Path.Combine(runsDir, runId + ".json");

            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            string activeRunId = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveRunId, string.Empty);
            if (string.Equals(activeRunId, runId, StringComparison.Ordinal))
            {
                return ProtocolJson.Serialize(new TestRunResultPayload
                {
                    runId = runId,
                    mode = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveMode, string.Empty),
                    status = "Running",
                    startedAt = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveStartedAt, string.Empty),
                    summary = new TestRunSummary
                    {
                        completed = SessionState.GetInt(ProtocolConstants.TestSessionKeyProgressCompleted, 0),
                        total = SessionState.GetInt(ProtocolConstants.TestSessionKeyProgressTotal, 0),
                    },
                });
            }

            throw new CommandFailureException(
                ProtocolConstants.ErrorTestRunNotFound,
                "runId '" + runId + "'에 해당하는 test run이 없습니다.");
        }

        private static string TryReadLastRunId()
        {
            try
            {
                string lastRunPath = Path.Combine(
                    Application.dataPath,
                    "..",
                    ProtocolConstants.TestLastRunFileRelative);
                if (!File.Exists(lastRunPath))
                {
                    return string.Empty;
                }

                string content = File.ReadAllText(lastRunPath);
                TestRunnerCallbacks_LastRunPointer? pointer =
                    ProtocolJson.Deserialize<TestRunnerCallbacks_LastRunPointer>(content);
                return pointer?.lastRunId ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetAssemblyName(ITestAdaptor test)
        {
            string fullName = test.TypeInfo?.Assembly?.FullName ?? string.Empty;
            int commaIndex = fullName.IndexOf(',');
            return commaIndex >= 0
                ? fullName.Substring(0, commaIndex).Trim()
                : fullName;
        }

        private static string GetRequestId(TaskCompletionSource<ResponseEnvelope> completion)
        {
            string? requestId = completion.Task.AsyncState as string;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new InvalidOperationException("Deferred test request ID is missing.");
            }

            return requestId;
        }

        private static bool TryBeginRun_Internal(out string activeRunId, out string activeMode)
        {
            lock (_activeLock)
            {
                if (_hasActiveRun)
                {
                    activeRunId = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveRunId, string.Empty);
                    activeMode = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveMode, string.Empty);
                    return false;
                }

                _hasActiveRun = true;
                activeRunId = string.Empty;
                activeMode = string.Empty;
                return true;
            }
        }

        internal static int ResolveTestTimeoutSeconds(TestRunArgs args)
        {
            return args.timeoutSeconds > 0
                ? Math.Min(args.timeoutSeconds, ProtocolConstants.MaxTestRunTimeoutSeconds)
                : ProtocolConstants.DefaultTestRunTimeoutSeconds;
        }

        internal static void BeginRun_PersistSession(
            string runId,
            string mode,
            int timeoutSeconds,
            bool noDomainReload)
        {
            SessionState.SetString(ProtocolConstants.TestSessionKeyActiveRunId, runId);
            SessionState.SetString(ProtocolConstants.TestSessionKeyActiveMode, mode);
            SessionState.SetString(ProtocolConstants.TestSessionKeyActiveStartedAt, DateTime.UtcNow.ToString("O"));
            SessionState.SetInt(ProtocolConstants.TestSessionKeyActiveTimeoutSeconds, timeoutSeconds);
            SessionState.SetBool(ProtocolConstants.TestSessionKeyActiveNoDomainReload, noDomainReload);
        }

        internal static void StoreActiveRunGuid(string runGuid)
        {
            SessionState.SetString(ProtocolConstants.TestSessionKeyActiveRunGuid, runGuid);
        }

        internal static void EndRun()
        {
            lock (_activeLock)
            {
                _hasActiveRun = false;
                SessionState.EraseString(ProtocolConstants.TestSessionKeyActiveRunId);
                SessionState.EraseString(ProtocolConstants.TestSessionKeyActiveMode);
                SessionState.EraseString(ProtocolConstants.TestSessionKeyActiveStartedAt);
                SessionState.EraseInt(ProtocolConstants.TestSessionKeyActiveTimeoutSeconds);
                SessionState.EraseString(ProtocolConstants.TestSessionKeyActiveRunGuid);
                SessionState.EraseBool(ProtocolConstants.TestSessionKeyActiveNoDomainReload);
                SessionState.EraseInt(ProtocolConstants.TestSessionKeyProgressCompleted);
                SessionState.EraseInt(ProtocolConstants.TestSessionKeyProgressTotal);
                SessionState.EraseInt(ProtocolConstants.TestSessionKeyCallbacksInstanceId);
            }
        }

        internal static void RestoreLockFromSession()
        {
            string activeRunId = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveRunId, string.Empty);
            if (string.IsNullOrEmpty(activeRunId))
            {
                return;
            }

            string activeMode = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveMode, string.Empty);
            int timeoutSeconds = GetActiveTimeoutSeconds();
            if (string.Equals(activeMode, "play", StringComparison.Ordinal)
                && IsActiveRunPastDeadline(timeoutSeconds))
            {
                MarkRestoredPlayModeRunTimedOut(activeRunId);
                return;
            }

            lock (_activeLock)
            {
                _hasActiveRun = true;
            }

            if (string.Equals(activeMode, "play", StringComparison.Ordinal))
            {
                RestorePlayModeRunFromSession(activeRunId, timeoutSeconds);
            }
        }

        internal static int GetActiveTimeoutSeconds()
        {
            int timeoutSeconds = SessionState.GetInt(
                ProtocolConstants.TestSessionKeyActiveTimeoutSeconds,
                ProtocolConstants.DefaultTestRunTimeoutSeconds);
            return timeoutSeconds > 0
                ? Math.Min(timeoutSeconds, ProtocolConstants.MaxTestRunTimeoutSeconds)
                : ProtocolConstants.DefaultTestRunTimeoutSeconds;
        }

        internal static bool TryGetActiveStartedAtUtc(out DateTime startedAtUtc)
        {
            string startedAtValue = SessionState.GetString(
                ProtocolConstants.TestSessionKeyActiveStartedAt,
                string.Empty);
            if (DateTime.TryParse(
                startedAtValue,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime parsed))
            {
                startedAtUtc = parsed.ToUniversalTime();
                return true;
            }

            startedAtUtc = DateTime.UtcNow;
            return false;
        }

        private static bool IsActiveRunPastDeadline(int timeoutSeconds)
        {
            if (!TryGetActiveStartedAtUtc(out DateTime startedAtUtc))
            {
                return false;
            }

            DateTime deadline = startedAtUtc.AddSeconds(timeoutSeconds + ProtocolConstants.TestRunCancelGraceSeconds);
            return DateTime.UtcNow >= deadline;
        }

        [Serializable]
        private sealed class TestRunnerCallbacks_LastRunPointer
        {
            public string lastRunId = string.Empty;
        }
    }
}
