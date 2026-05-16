#nullable enable
using System;
using System.Threading.Tasks;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class TestCommandHandler
    {
        private static TestRunnerApi? _playModeApi;
        private static TestRunnerCallbacks? _playModeCallbacks;
        private static EditorApplication.CallbackFunction? _playModeWatchdog;

        private void StartPlayModeRun(
            TestRunArgs args,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ResolveFilterFullNamesThenStart(
                TestMode.PlayMode,
                args,
                completion,
                projectHash,
                requestId,
                stopwatch,
                resolvedTestNames => StartPlayModeRunResolved(
                    args,
                    completion,
                    projectHash,
                    requestId,
                    resolvedTestNames));
        }

        private void StartPlayModeRunResolved(
            TestRunArgs args,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId,
            string[]? resolvedTestNames)
        {
            string runId = Guid.NewGuid().ToString("N");
            int timeoutSec = ResolveTestTimeoutSeconds(args);
            BeginRun_PersistSession(runId, "play", timeoutSec, args.noDomainReload);

            if (args.noDomainReload)
            {
                DomainReloadDisableScope.Activate();
            }

            TryGetActiveStartedAtUtc(out DateTime startedAtUtc);
            var callbacks = TestRunnerCallbacks.Create(runId, "play", args.noDomainReload, startedAtUtc);
            callbacks.StoreInstanceIdToSession();
            RegisterPlayModeCallbacks(callbacks);

            try
            {
                string runGuid = _playModeApi!.Execute(new ExecutionSettings(BuildFilter(args, TestMode.PlayMode, resolvedTestNames)));
                StoreActiveRunGuid(runGuid);
            }
            catch (Exception exception)
            {
                callbacks.MarkFailed("PlayMode 시작 실패: " + exception.Message);
                CleanupPlayModeRegistration();

                completion.TrySetResult(ResponseEnvelope.Failure(
                    requestId,
                    projectHash,
                    "TEST_RUN_START_FAILED",
                    "PlayMode test run을 시작할 수 없습니다: " + exception.Message,
                    false,
                    0,
                    ProtocolConstants.TransportLive,
                    null));
                return;
            }

            StartPlayModeWatchdog(runId, timeoutSec, completion, projectHash, requestId);
        }

        internal static void RestorePlayModeRunFromSession(string runId, int timeoutSeconds)
        {
            EnsurePlayModeCallbacksFromSession(runId);
            StartPlayModeWatchdog(runId, timeoutSeconds, null, null, null);
        }

        internal static void MarkRestoredPlayModeRunTimedOut(string runId)
        {
            TryCancelActiveUnityTestRun();
            TestRunnerCallbacks callbacks = EnsureCallbacksForCompletion(runId, "play");
            callbacks.MarkTimedOut();
            DestroyIfNotRegisteredPlayModeCallback(callbacks);
            CleanupPlayModeRegistration();
        }

        private static void RegisterPlayModeCallbacks(TestRunnerCallbacks callbacks)
        {
            CleanupPlayModeRegistration();

            _playModeApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _playModeApi.hideFlags = HideFlags.HideAndDontSave;
            _playModeCallbacks = callbacks;
            _playModeApi.RegisterCallbacks(callbacks);
        }

        private static TestRunnerCallbacks EnsurePlayModeCallbacksFromSession(string runId)
        {
            TestRunnerCallbacks? callbacks = TestRunnerCallbacks.TryFindFromSession();
            if (callbacks == null || !string.Equals(callbacks.RunId, runId, StringComparison.Ordinal))
            {
                callbacks = CreateCallbacksFromActiveSession(runId, "play");
                callbacks.StoreInstanceIdToSession();
            }

            RegisterPlayModeCallbacks(callbacks);
            return callbacks;
        }

        private static TestRunnerCallbacks EnsureCallbacksForCompletion(string runId, string mode)
        {
            TestRunnerCallbacks? callbacks = TestRunnerCallbacks.TryFindFromSession();
            if (callbacks != null && string.Equals(callbacks.RunId, runId, StringComparison.Ordinal))
            {
                return callbacks;
            }

            callbacks = CreateCallbacksFromActiveSession(runId, mode);
            callbacks.StoreInstanceIdToSession();
            return callbacks;
        }

        private static TestRunnerCallbacks CreateCallbacksFromActiveSession(string runId, string mode)
        {
            TryGetActiveStartedAtUtc(out DateTime startedAtUtc);
            bool noDomainReload = SessionState.GetBool(
                ProtocolConstants.TestSessionKeyActiveNoDomainReload,
                false);
            return TestRunnerCallbacks.Create(runId, mode, noDomainReload, startedAtUtc);
        }

        private static void StartPlayModeWatchdog(
            string runId,
            int timeoutSec,
            TaskCompletionSource<ResponseEnvelope>? completion,
            string? projectHash,
            string? requestId)
        {
            StopPlayModeWatchdog();

            TryGetActiveStartedAtUtc(out DateTime startedAtUtc);
            DateTime entryDeadline = DateTime.UtcNow.AddSeconds(ProtocolConstants.TestPlayModeEntryTimeoutSeconds);
            DateTime cancelAt = startedAtUtc.AddSeconds(timeoutSec);
            DateTime deadline = cancelAt.AddSeconds(ProtocolConstants.TestRunCancelGraceSeconds);
            bool startResponseSent = completion == null;
            bool cancelRequested = false;

            void Poll()
            {
                if (!startResponseSent && completion!.Task.IsCompleted)
                {
                    startResponseSent = true;
                }

                string activeRunId = SessionState.GetString(
                    ProtocolConstants.TestSessionKeyActiveRunId,
                    string.Empty);
                if (!string.Equals(activeRunId, runId, StringComparison.Ordinal))
                {
                    if (!startResponseSent && TestRunCompletedOnDisk(runId, out string? finalJson))
                    {
                        completion!.TrySetResult(ResponseEnvelope.Success(
                            requestId!,
                            projectHash,
                            finalJson,
                            0,
                            ProtocolConstants.TransportLive));
                    }
                    else if (!startResponseSent)
                    {
                        completion!.TrySetResult(ResponseEnvelope.Failure(
                            requestId!,
                            projectHash,
                            "TEST_RUN_START_FAILED",
                            "PlayMode test run이 STARTED 응답 전에 종료되었습니다.",
                            false,
                            0,
                            ProtocolConstants.TransportLive,
                            null));
                    }

                    CleanupPlayModeRegistration();
                    return;
                }

                if (!startResponseSent && TestRunCompletedOnDisk(runId, out string? completedJson))
                {
                    startResponseSent = true;
                    completion!.TrySetResult(ResponseEnvelope.Success(
                        requestId!,
                        projectHash,
                        completedJson,
                        0,
                        ProtocolConstants.TransportLive));
                }

                if (!startResponseSent
                    && (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    startResponseSent = true;
                    completion!.TrySetResult(BuildPlayModeStartedResponse(requestId!, projectHash, runId));
                }

                if (!startResponseSent && DateTime.UtcNow >= entryDeadline)
                {
                    string reason = BuildPlayModeEntryFailureMessage();
                    TryCancelActiveUnityTestRun();
                    TestRunnerCallbacks callbacks = EnsureCallbacksForCompletion(runId, "play");
                    callbacks.MarkFailed(reason);
                    DestroyIfNotRegisteredPlayModeCallback(callbacks);
                    completion!.TrySetResult(ResponseEnvelope.Failure(
                        requestId!,
                        projectHash,
                        ProtocolConstants.ErrorTestPlayModeEntryFailed,
                        reason,
                        false,
                        0,
                        ProtocolConstants.TransportLive,
                        null));
                    CleanupPlayModeRegistration();
                    return;
                }

                DateTime now = DateTime.UtcNow;
                if (!cancelRequested && now >= cancelAt)
                {
                    cancelRequested = true;
                    TryCancelActiveUnityTestRun();
                }

                if (now >= deadline)
                {
                    TestRunnerCallbacks callbacks = EnsureCallbacksForCompletion(runId, "play");
                    callbacks.MarkTimedOut();
                    DestroyIfNotRegisteredPlayModeCallback(callbacks);

                    if (!startResponseSent)
                    {
                        completion!.TrySetResult(ResponseEnvelope.Failure(
                            requestId!,
                            projectHash,
                            ProtocolConstants.ErrorTestTimeout,
                            "PlayMode test run이 " + timeoutSec + "초 내 완료되지 않았습니다.",
                            false,
                            0,
                            ProtocolConstants.TransportLive,
                            null));
                    }

                    CleanupPlayModeRegistration();
                }
            }

            _playModeWatchdog = Poll;
            EditorApplication.update += _playModeWatchdog;
            Poll();
        }

        private static ResponseEnvelope BuildPlayModeStartedResponse(
            string requestId,
            string? projectHash,
            string runId)
        {
            var startedPayload = new TestRunStartedPayload
            {
                runId = runId,
                mode = "play",
                status = "STARTED",
                startedAt = SessionState.GetString(
                    ProtocolConstants.TestSessionKeyActiveStartedAt,
                    DateTime.UtcNow.ToString("O")),
            };
            return ResponseEnvelope.Success(
                requestId,
                projectHash,
                ProtocolJson.Serialize(startedPayload),
                0,
                ProtocolConstants.TransportLive);
        }

        private static string BuildPlayModeEntryFailureMessage()
        {
            var scene = EditorSceneManager.GetActiveScene();
            return "PlayMode test run이 "
                + ProtocolConstants.TestPlayModeEntryTimeoutSeconds
                + "초 안에 PlayMode 진입을 시작하지 못했습니다."
                + " isCompiling=" + EditorApplication.isCompiling
                + ", isUpdating=" + EditorApplication.isUpdating
                + ", isPlaying=" + EditorApplication.isPlaying
                + ", isPlayingOrWillChangePlaymode=" + EditorApplication.isPlayingOrWillChangePlaymode
                + ", activeScenePath=" + scene.path
                + ", activeSceneDirty=" + scene.isDirty
                + ".";
        }

        private static void TryCancelActiveUnityTestRun()
        {
            string runGuid = SessionState.GetString(
                ProtocolConstants.TestSessionKeyActiveRunGuid,
                string.Empty);
            if (string.IsNullOrEmpty(runGuid))
            {
                return;
            }

            try
            {
                TestRunnerApi.CancelTestRun(runGuid);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[unity-cli-bridge] Failed to cancel test run " + runGuid + ": " + exception.Message);
            }
        }

        private static void StopPlayModeWatchdog()
        {
            if (_playModeWatchdog == null)
            {
                return;
            }

            EditorApplication.update -= _playModeWatchdog;
            _playModeWatchdog = null;
        }

        private static void DestroyIfNotRegisteredPlayModeCallback(TestRunnerCallbacks callbacks)
        {
            if (!ReferenceEquals(callbacks, _playModeCallbacks))
            {
                UnityEngine.Object.DestroyImmediate(callbacks);
            }
        }

        private static void CleanupPlayModeRegistration()
        {
            StopPlayModeWatchdog();

            if (_playModeApi != null && _playModeCallbacks != null)
            {
                try
                {
                    _playModeApi.UnregisterCallbacks(_playModeCallbacks);
                }
                catch
                {
                }
            }

            if (_playModeApi != null)
            {
                UnityEngine.Object.DestroyImmediate(_playModeApi);
            }

            if (_playModeCallbacks != null)
            {
                UnityEngine.Object.DestroyImmediate(_playModeCallbacks);
            }

            _playModeApi = null;
            _playModeCallbacks = null;
        }
    }

    internal static class DomainReloadDisableScope
    {
        public static void Activate()
        {
            bool wasEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            EnterPlayModeOptions previousFlags = EditorSettings.enterPlayModeOptions;

            SessionState.SetBool(ProtocolConstants.TestSessionKeyScopeWasEnabled, wasEnabled);
            SessionState.SetInt(ProtocolConstants.TestSessionKeyScopePreviousFlags, (int)previousFlags);
            SessionState.SetBool(ProtocolConstants.TestSessionKeyScopeActive, true);

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions =
                previousFlags
                | EnterPlayModeOptions.DisableDomainReload
                | EnterPlayModeOptions.DisableSceneReload;
        }

        public static void Deactivate()
        {
            if (!SessionState.GetBool(ProtocolConstants.TestSessionKeyScopeActive, false))
            {
                return;
            }

            EditorSettings.enterPlayModeOptionsEnabled =
                SessionState.GetBool(ProtocolConstants.TestSessionKeyScopeWasEnabled, false);
            EditorSettings.enterPlayModeOptions =
                (EnterPlayModeOptions)SessionState.GetInt(ProtocolConstants.TestSessionKeyScopePreviousFlags, 0);
            SessionState.SetBool(ProtocolConstants.TestSessionKeyScopeActive, false);
        }

        public static void RestoreIfOrphaned()
        {
            if (!SessionState.GetBool(ProtocolConstants.TestSessionKeyScopeActive, false))
            {
                return;
            }

            string activeRunId = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveRunId, string.Empty);
            if (string.IsNullOrEmpty(activeRunId))
            {
                Deactivate();
            }
        }
    }
}
