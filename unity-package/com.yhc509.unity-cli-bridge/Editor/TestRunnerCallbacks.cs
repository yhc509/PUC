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
            return Create(runId, mode, domainReloadDisabled, DateTime.UtcNow);
        }

        public static TestRunnerCallbacks Create(
            string runId,
            string mode,
            bool domainReloadDisabled,
            DateTime startedAtUtc)
        {
            var instance = CreateInstance<TestRunnerCallbacks>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance._runId = runId;
            instance._mode = mode;
            instance._startedAtUtcTicks = startedAtUtc.ToUniversalTime().Ticks;
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
            AppendLeafResult(result);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            _runFinished = true;
            AppendLeafResults(result);
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
            List<string> warnings = BuildWarnings(extraWarnings);
            TestRunResultPayload payload = BuildPayload(status, warnings);

            try
            {
                FlushToDisk(payload);
            }
            catch (Exception exception)
            {
                Debug.LogError("[unity-cli-bridge] Failed to flush test run " + _runId + ": " + exception);

                warnings.Add("Failed to write result cache: " + exception.Message);
                TestRunResultPayload failedPayload = BuildPayload("Failed", warnings);
                StoreInlineResult(failedPayload);

                try
                {
                    FlushToDisk(failedPayload);
                }
                catch (Exception retryException)
                {
                    Debug.LogError("[unity-cli-bridge] Failed to flush fallback test run " + _runId + ": " + retryException);
                }
            }
            finally
            {
                if (_domainReloadDisabled)
                {
                    DomainReloadDisableScope.Deactivate();
                }

                TestCommandHandler.EndRun();
            }
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

        private void AppendLeafResults(ITestResultAdaptor result)
        {
            if (result.Test == null)
            {
                return;
            }

            if (!result.Test.IsSuite)
            {
                AppendLeafResult(result);
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                AppendLeafResults(child);
            }

            if (_totalCount < _completedCount)
            {
                _totalCount = _completedCount;
                SessionState.SetInt(ProtocolConstants.TestSessionKeyProgressTotal, _totalCount);
            }
        }

        private void AppendLeafResult(ITestResultAdaptor result)
        {
            if (result.Test == null || result.Test.IsSuite)
            {
                return;
            }

            string fullName = result.Test.FullName ?? string.Empty;
            for (int index = 0; index < _results.Count; index++)
            {
                if (string.Equals(_results[index].fullName, fullName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            var entry = new TestResultEntry
            {
                fullName = fullName,
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

            if (_totalCount < _completedCount)
            {
                _totalCount = _completedCount;
                SessionState.SetInt(ProtocolConstants.TestSessionKeyProgressTotal, _totalCount);
            }

            SessionState.SetInt(ProtocolConstants.TestSessionKeyProgressCompleted, _completedCount);
        }

        private List<string> BuildWarnings(string[]? extraWarnings)
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

            return warnings;
        }

        private TestRunResultPayload BuildPayload(string status, List<string> warnings)
        {
            return new TestRunResultPayload
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
        }

        private static void StoreInlineResult(TestRunResultPayload payload)
        {
            SessionState.SetString(ProtocolConstants.TestSessionKeyInlineResultRunId, payload.runId);
            SessionState.SetString(ProtocolConstants.TestSessionKeyInlineResultJson, ProtocolJson.Serialize(payload));
        }

        private void FlushToDisk(TestRunResultPayload payload)
        {
            string projectRoot = Path.Combine(Application.dataPath, "..");
            string runsDir = Path.Combine(projectRoot, ProtocolConstants.TestRunsDirectoryRelative);
            string finalPath = Path.Combine(runsDir, _runId + ".json");
            AtomicFileUtility.WriteAllText(finalPath, ProtocolJson.Serialize(payload));

            string lastRunPath = Path.Combine(projectRoot, ProtocolConstants.TestLastRunFileRelative);
            AtomicFileUtility.WriteAllText(lastRunPath, ProtocolJson.Serialize(new TestLastRunPointer { lastRunId = _runId }));
        }

        [Serializable]
        private sealed class TestLastRunPointer
        {
            public string lastRunId = string.Empty;
        }
    }
}
