#nullable enable
using System;
using System.Threading.Tasks;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class TestCommandHandler
    {
        private void StartPlayModeRun(
            TestRunArgs args,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId)
        {
            string runId = Guid.NewGuid().ToString("N");
            BeginRun_PersistSession(runId, "play");

            if (args.noDomainReload)
            {
                DomainReloadDisableScope.Activate();
            }

            var callbacks = TestRunnerCallbacks.Create(runId, "play", args.noDomainReload);
            callbacks.StoreInstanceIdToSession();

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.hideFlags = HideFlags.HideAndDontSave;
            api.RegisterCallbacks(callbacks);

            try
            {
                api.Execute(new ExecutionSettings(BuildFilter(args, TestMode.PlayMode)));
            }
            catch (Exception exception)
            {
                callbacks.MarkFailed("PlayMode 시작 실패: " + exception.Message);

                try
                {
                    api.UnregisterCallbacks(callbacks);
                }
                catch
                {
                }

                UnityEngine.Object.DestroyImmediate(api);
                UnityEngine.Object.DestroyImmediate(callbacks);

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

            var startedPayload = new TestRunStartedPayload
            {
                runId = runId,
                mode = "play",
                status = "STARTED",
                startedAt = SessionState.GetString(ProtocolConstants.TestSessionKeyActiveStartedAt, DateTime.UtcNow.ToString("O")),
            };
            completion.TrySetResult(ResponseEnvelope.Success(
                requestId,
                projectHash,
                ProtocolJson.Serialize(startedPayload),
                0,
                ProtocolConstants.TransportLive));
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
