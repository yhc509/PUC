#nullable enable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class TestCommandHandler
    {
        private void StartEditModeRun(
            TestRunArgs args,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId)
        {
            string runId = Guid.NewGuid().ToString("N");
            BeginRun_PersistSession(runId, "edit");

            int timeoutSec = args.timeoutSeconds > 0
                ? Math.Min(args.timeoutSeconds, ProtocolConstants.MaxTestRunTimeoutSeconds)
                : ProtocolConstants.DefaultTestRunTimeoutSeconds;

            var callbacks = TestRunnerCallbacks.Create(runId, "edit", false);
            callbacks.StoreInstanceIdToSession();

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(callbacks);

            void Cleanup()
            {
                try
                {
                    api.UnregisterCallbacks(callbacks);
                }
                catch
                {
                }

                UnityEngine.Object.DestroyImmediate(api);
                UnityEngine.Object.DestroyImmediate(callbacks);
            }

            var stopwatch = Stopwatch.StartNew();
            string runGuid;
            try
            {
                runGuid = api.Execute(new ExecutionSettings(BuildFilter(args, TestMode.EditMode)));
            }
            catch
            {
                stopwatch.Stop();
                Cleanup();
                throw;
            }

            bool cancelRequested = false;
            DateTime cancelRequestedAt = DateTime.MinValue;

            void Poll()
            {
                if (completion.Task.IsCompleted)
                {
                    EditorApplication.update -= Poll;
                    Cleanup();
                    return;
                }

                if (TestRunCompletedOnDisk(runId, out string? resultJson))
                {
                    EditorApplication.update -= Poll;
                    stopwatch.Stop();
                    completion.TrySetResult(ResponseEnvelope.Success(
                        requestId,
                        projectHash,
                        resultJson,
                        stopwatch.ElapsedMilliseconds,
                        ProtocolConstants.TransportLive));
                    Cleanup();
                    return;
                }

                if (cancelRequested)
                {
                    double cancelSeconds = (DateTime.UtcNow - cancelRequestedAt).TotalSeconds;
                    if (cancelSeconds <= ProtocolConstants.TestRunCancelGraceSeconds)
                    {
                        return;
                    }

                    callbacks.MarkTimedOut();
                    EditorApplication.update -= Poll;
                    stopwatch.Stop();
                    if (TestRunCompletedOnDisk(runId, out string? finalJson))
                    {
                        completion.TrySetResult(ResponseEnvelope.Success(
                            requestId,
                            projectHash,
                            finalJson,
                            stopwatch.ElapsedMilliseconds,
                            ProtocolConstants.TransportLive));
                    }
                    else
                    {
                        completion.TrySetResult(ResponseEnvelope.Failure(
                            requestId,
                            projectHash,
                            ProtocolConstants.ErrorTestTimeout,
                            "EditMode test run이 " + timeoutSec + "초 내 완료되지 않았습니다.",
                            false,
                            stopwatch.ElapsedMilliseconds,
                            ProtocolConstants.TransportLive,
                            null));
                        EndRun();
                    }

                    Cleanup();
                    return;
                }

                if (stopwatch.Elapsed.TotalSeconds > timeoutSec)
                {
                    cancelRequested = true;
                    cancelRequestedAt = DateTime.UtcNow;
                    TestRunnerApi.CancelTestRun(runGuid);
                }
            }

            EditorApplication.update += Poll;
            Poll();
        }

        private static bool TestRunCompletedOnDisk(string runId, out string? json)
        {
            string runsDir = System.IO.Path.Combine(
                Application.dataPath,
                "..",
                ProtocolConstants.TestRunsDirectoryRelative);
            string path = System.IO.Path.Combine(runsDir, runId + ".json");
            if (System.IO.File.Exists(path))
            {
                json = System.IO.File.ReadAllText(path);
                return true;
            }

            json = null;
            return false;
        }

        internal static Filter BuildFilter(TestRunArgs args, TestMode mode)
        {
            var filter = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(args.filter))
            {
                filter.testNames = new[] { args.filter };
            }

            if (!string.IsNullOrEmpty(args.category))
            {
                filter.categoryNames = new[] { args.category };
            }

            if (!string.IsNullOrEmpty(args.assembly))
            {
                filter.assemblyNames = new[] { args.assembly };
            }

            return filter;
        }
    }
}
