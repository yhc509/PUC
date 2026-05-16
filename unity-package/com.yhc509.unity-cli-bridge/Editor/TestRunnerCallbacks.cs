#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed class TestRunnerCallbacks : ScriptableObject, ICallbacks
    {
        [SerializeField] private string _runId = string.Empty;
        [SerializeField] private string _mode = string.Empty;
        [SerializeField] private long _startedAtUtcTicks;
        [SerializeField] private bool _domainReloadDisabled;
        [SerializeField] private List<TestResultEntry> _results = new List<TestResultEntry>();
        [SerializeField] private int _completedCount;
        [SerializeField] private int _totalCount;
        [SerializeField] private int _passedCount;
        [SerializeField] private int _failedCount;
        [SerializeField] private int _skippedCount;
        [SerializeField] private int _inconclusiveCount;
        [SerializeField] private bool _runFinished;

        public string RunId => _runId;
        public string Mode => _mode;

        public static TestRunnerCallbacks Create(string runId, string mode, bool domainReloadDisabled)
        {
            var instance = CreateInstance<TestRunnerCallbacks>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance._runId = runId;
            instance._mode = mode;
            instance._startedAtUtcTicks = DateTime.UtcNow.Ticks;
            instance._domainReloadDisabled = domainReloadDisabled;
            return instance;
        }

        public static TestRunnerCallbacks? TryFindFromSession()
        {
            int instanceId = SessionState.GetInt(ProtocolConstants.TestSessionKeyCallbacksInstanceId, 0);
            if (instanceId == 0)
            {
                return null;
            }

            return EditorUtility.InstanceIDToObject(instanceId) as TestRunnerCallbacks;
        }

        public void StoreInstanceIdToSession()
        {
            SessionState.SetInt(ProtocolConstants.TestSessionKeyCallbacksInstanceId, GetInstanceID());
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            _totalCount = CountLeafTests(testsToRun);
            SessionState.SetInt(ProtocolConstants.TestSessionKeyProgressTotal, _totalCount);
            SessionState.SetInt(ProtocolConstants.TestSessionKeyProgressCompleted, 0);
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.Test == null || result.Test.IsSuite)
            {
                return;
            }

            var entry = new TestResultEntry
            {
                fullName = result.Test.FullName ?? string.Empty,
                assembly = GetAssemblyName(result.Test),
                categories = result.Test.Categories ?? Array.Empty<string>(),
                outcome = result.TestStatus.ToString(),
                durationMs = (long)(result.Duration * 1000),
                message = result.Message ?? string.Empty,
                stackTrace = result.StackTrace ?? string.Empty,
            };
            _results.Add(entry);
            _completedCount++;

            switch (result.TestStatus)
            {
                case TestStatus.Passed:
                    _passedCount++;
                    break;
                case TestStatus.Failed:
                    _failedCount++;
                    break;
                case TestStatus.Skipped:
                    _skippedCount++;
                    break;
                case TestStatus.Inconclusive:
                    _inconclusiveCount++;
                    break;
            }

            SessionState.SetInt(ProtocolConstants.TestSessionKeyProgressCompleted, _completedCount);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            _runFinished = true;
            CompleteRun("Completed");
        }

        public void MarkCancelled()
        {
            CompleteRun("Cancelled");
        }

        public void MarkTimedOut()
        {
            CompleteRun("TimedOut");
        }

        public void MarkFailed(string reason)
        {
            CompleteRun("Failed", new[] { reason });
        }

        private void CompleteRun(string status, string[]? extraWarnings = null)
        {
            FlushToDisk(status, extraWarnings);
            if (_domainReloadDisabled)
            {
                DomainReloadDisableScope.Deactivate();
            }

            TestCommandHandler.EndRun();
        }

        private static int CountLeafTests(ITestAdaptor adaptor)
        {
            if (!adaptor.IsSuite)
            {
                return 1;
            }

            int count = 0;
            foreach (ITestAdaptor child in adaptor.Children)
            {
                count += CountLeafTests(child);
            }

            return count;
        }

        private static string GetAssemblyName(ITestAdaptor test)
        {
            string fullName = test.TypeInfo?.Assembly?.FullName ?? string.Empty;
            int commaIndex = fullName.IndexOf(',');
            return commaIndex >= 0
                ? fullName.Substring(0, commaIndex).Trim()
                : fullName;
        }

        private void FlushToDisk(string status, string[]? extraWarnings = null)
        {
            try
            {
                var warnings = new List<string>();
                if (_domainReloadDisabled)
                {
                    warnings.Add("domain reload disabled; static state may persist between runs");
                }

                if (extraWarnings != null)
                {
                    warnings.AddRange(extraWarnings);
                }

                var payload = new TestRunResultPayload
                {
                    runId = _runId,
                    mode = _mode,
                    status = status,
                    startedAt = new DateTime(_startedAtUtcTicks, DateTimeKind.Utc).ToString("O"),
                    durationMs = (long)((DateTime.UtcNow.Ticks - _startedAtUtcTicks) / TimeSpan.TicksPerMillisecond),
                    summary = new TestRunSummary
                    {
                        total = _totalCount,
                        passed = _passedCount,
                        failed = _failedCount,
                        skipped = _skippedCount,
                        inconclusive = _inconclusiveCount,
                        completed = _completedCount,
                    },
                    tests = _results.ToArray(),
                    warnings = warnings.ToArray(),
                };

                string projectRoot = Path.Combine(Application.dataPath, "..");
                string runsDir = Path.Combine(projectRoot, ProtocolConstants.TestRunsDirectoryRelative);
                Directory.CreateDirectory(runsDir);

                string finalPath = Path.Combine(runsDir, _runId + ".json");
                string tempPath = finalPath + ".tmp";

                File.WriteAllText(tempPath, ProtocolJson.Serialize(payload));
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(tempPath, finalPath);

                string lastRunPath = Path.Combine(projectRoot, ProtocolConstants.TestLastRunFileRelative);
                string? lastRunDir = Path.GetDirectoryName(lastRunPath);
                if (!string.IsNullOrEmpty(lastRunDir))
                {
                    Directory.CreateDirectory(lastRunDir);
                }

                string lastRunTemp = lastRunPath + ".tmp";
                File.WriteAllText(lastRunTemp, ProtocolJson.Serialize(new TestLastRunPointer { lastRunId = _runId }));
                if (File.Exists(lastRunPath))
                {
                    File.Delete(lastRunPath);
                }

                File.Move(lastRunTemp, lastRunPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[unity-cli-bridge] Failed to flush test run " + _runId + ": " + exception);
            }
        }

        [Serializable]
        private sealed class TestLastRunPointer
        {
            public string lastRunId = string.Empty;
        }
    }
}
