#nullable enable
using System;
using System.IO;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed class RecordCommandHandler
    {
        private static readonly object _activeLock = new object();
        private static bool _hasActiveRecording;
        private static RecorderController? _controller;
        private static string? _recordingId;
        private static string? _targetPath;
        private static string? _outputBasePath;
        private static DateTime _startedAtUtc;
        private static int _durationSeconds;
        private static int _fps;
        private static int _width;
        private static int _height;

        public bool CanHandle(string command)
        {
            return command == ProtocolConstants.CommandRecordStart
                || command == ProtocolConstants.CommandRecordStop
                || command == ProtocolConstants.CommandRecordStatus;
        }

        public string Handle(string command, string argumentsJson)
        {
            if (command == ProtocolConstants.CommandRecordStart)
            {
                return HandleStart(argumentsJson);
            }

            if (command == ProtocolConstants.CommandRecordStop)
            {
                return HandleStop();
            }

            return HandleStatus(argumentsJson);
        }

        internal static string StartForSequence(string? path)
        {
            return StartRecording(new RecordStartArgs
            {
                path = path,
                fps = ProtocolConstants.DefaultRecordFps,
            });
        }

        internal static string? StopForSequenceIfActive()
        {
            if (!HasActiveRecording())
            {
                return null;
            }

            string json = FinalizeAndBuildResult("Completed");
            RecordResultPayload? payload = ProtocolJson.Deserialize<RecordResultPayload>(json);
            return payload == null || string.IsNullOrWhiteSpace(payload.path) ? null : payload.path;
        }

        private static string HandleStart(string argumentsJson)
        {
            RecordStartArgs args = ProtocolJson.Deserialize<RecordStartArgs>(argumentsJson) ?? new RecordStartArgs();
            return StartRecording(args);
        }

        private static string HandleStop()
        {
            if (!HasActiveRecording() || _controller == null || string.IsNullOrWhiteSpace(_recordingId))
            {
                throw new CommandFailureException(
                    ProtocolConstants.ErrorRecordNotActive,
                    "No recording is active.");
            }

            return FinalizeAndBuildResult("Completed");
        }

        private static string HandleStatus(string argumentsJson)
        {
            RecordStatusArgs args = ProtocolJson.Deserialize<RecordStatusArgs>(argumentsJson) ?? new RecordStatusArgs();
            string? id = string.IsNullOrWhiteSpace(args.recordingId) ? _recordingId : args.recordingId;

            if (HasActiveRecording()
                && _recordingId != null
                && (id == null || string.Equals(id, _recordingId, StringComparison.Ordinal)))
            {
                var live = new RecordResultPayload
                {
                    recordingId = _recordingId,
                    status = "Recording",
                    path = _targetPath ?? ((_outputBasePath ?? string.Empty) + ".mp4"),
                    durationMs = (long)(DateTime.UtcNow - _startedAtUtc).TotalMilliseconds,
                    fps = _fps,
                    width = _width,
                    height = _height,
                };
                return ProtocolJson.Serialize(live);
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                string sidecar = SidecarPath(id!);
                if (File.Exists(sidecar))
                {
                    return File.ReadAllText(sidecar);
                }
            }

            return ProtocolJson.Serialize(new RecordResultPayload
            {
                recordingId = id ?? string.Empty,
                status = "NotFound",
            });
        }

        private static string StartRecording(RecordStartArgs args)
        {
            if (!EditorApplication.isPlaying)
            {
                throw new CommandFailureException(
                    ProtocolConstants.ErrorRecordRequiresPlaymode,
                    "record start requires Play Mode. Enter Play Mode first (e.g. `unity-cli play`).");
            }

            if (!TryBeginRecording())
            {
                throw new CommandFailureException(
                    ProtocolConstants.ErrorRecordInProgress,
                    "A recording is already in progress. Stop it with `record stop` first.");
            }

            try
            {
                string recordingId = Guid.NewGuid().ToString("N");
                int fps = args.fps > 0 ? args.fps : ProtocolConstants.DefaultRecordFps;
                int durationSeconds = args.durationSeconds > 0
                    ? Math.Min(args.durationSeconds, ProtocolConstants.MaxRecordDurationSeconds)
                    : 0;

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string recordingsDir = Path.Combine(projectRoot, ProtocolConstants.RecordingsDirectoryRelative);
                Directory.CreateDirectory(recordingsDir);
                string outputBase = Path.Combine(recordingsDir, recordingId);

                Vector2 gameView = GetGameViewResolution();
                int width = MakeEven((int)gameView.x);
                int height = MakeEven((int)gameView.y);
                if (args.maxWidth > 0 && width > args.maxWidth)
                {
                    float scale = (float)args.maxWidth / width;
                    width = MakeEven(args.maxWidth);
                    height = MakeEven(Mathf.Max(2, (int)(height * scale)));
                }

                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movieSettings.name = "UCB Recording";
                movieSettings.Enabled = true;
                movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                movieSettings.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = width,
                    OutputHeight = height,
                };
                movieSettings.OutputFile = outputBase;

                controllerSettings.AddRecorderSettings(movieSettings);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = fps;
                RecorderOptions.VerboseMode = false;

                _controller = new RecorderController(controllerSettings);
                _controller.PrepareRecording();
                if (!_controller.StartRecording())
                {
                    throw new CommandFailureException("RECORD_START_FAILED", "Unity Recorder failed to start recording.");
                }

                _recordingId = recordingId;
                _targetPath = string.IsNullOrWhiteSpace(args.path) ? null : args.path;
                _outputBasePath = outputBase;
                _startedAtUtc = DateTime.UtcNow;
                _durationSeconds = durationSeconds;
                _fps = fps;
                _width = width;
                _height = height;
                PersistSession();
                RegisterAutoStopPoll();

                return ProtocolJson.Serialize(new RecordStartedPayload
                {
                    recordingId = recordingId,
                    status = "STARTED",
                    targetPath = _targetPath ?? (outputBase + ".mp4"),
                    startedAt = _startedAtUtc.ToString("O"),
                    durationSeconds = durationSeconds,
                });
            }
            catch
            {
                ClearState();
                throw;
            }
        }

        private static string FinalizeAndBuildResult(string status)
        {
            string recordingId = _recordingId
                ?? throw new CommandFailureException(ProtocolConstants.ErrorRecordNotActive, "No recording is active.");
            string producedPath = (_outputBasePath ?? string.Empty) + ".mp4";
            string finalPath = producedPath;

            try
            {
                _controller?.StopRecording();
            }
            finally
            {
                _controller = null;
            }

            if (!string.IsNullOrWhiteSpace(_targetPath))
            {
                string resolved = _targetPath!;
                string? directory = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                if (File.Exists(resolved))
                {
                    File.Delete(resolved);
                }

                if (File.Exists(producedPath))
                {
                    File.Move(producedPath, resolved);
                    finalPath = resolved;
                }
            }

            var fileInfo = new FileInfo(finalPath);
            var result = new RecordResultPayload
            {
                recordingId = recordingId,
                status = status,
                path = finalPath,
                durationMs = (long)(DateTime.UtcNow - _startedAtUtc).TotalMilliseconds,
                fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                fps = _fps,
                width = _width,
                height = _height,
            };

            WriteSidecar(recordingId, result);
            ClearState();
            return ProtocolJson.Serialize(result);
        }

        private static Vector2 GetGameViewResolution()
        {
            Vector2 size = Handles.GetMainGameViewSize();
            if (size.x < 2 || size.y < 2)
            {
                return new Vector2(1280, 720);
            }

            return size;
        }

        private static int MakeEven(int value)
        {
            int clamped = Math.Max(2, value);
            return clamped % 2 == 0 ? clamped : clamped - 1;
        }

        private static void PersistSession()
        {
            SessionState.SetString(ProtocolConstants.RecordSessionKeyActiveId, _recordingId ?? string.Empty);
            SessionState.SetString(ProtocolConstants.RecordSessionKeyTargetPath, _outputBasePath ?? string.Empty);
            SessionState.SetString(ProtocolConstants.RecordSessionKeyStartedAt, _startedAtUtc.ToString("O"));
            SessionState.SetInt(ProtocolConstants.RecordSessionKeyDurationSeconds, _durationSeconds);
        }

        private static void ClearState()
        {
            lock (_activeLock)
            {
                _hasActiveRecording = false;
            }

            _controller = null;
            _recordingId = null;
            _targetPath = null;
            _outputBasePath = null;
            _startedAtUtc = default(DateTime);
            _durationSeconds = 0;
            _fps = 0;
            _width = 0;
            _height = 0;
            SessionState.EraseString(ProtocolConstants.RecordSessionKeyActiveId);
            SessionState.EraseString(ProtocolConstants.RecordSessionKeyTargetPath);
            SessionState.EraseString(ProtocolConstants.RecordSessionKeyStartedAt);
            SessionState.EraseInt(ProtocolConstants.RecordSessionKeyDurationSeconds);
        }

        [InitializeOnLoadMethod]
        private static void RestoreFromSessionOnLoad()
        {
            string id = SessionState.GetString(ProtocolConstants.RecordSessionKeyActiveId, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            string basePath = SessionState.GetString(ProtocolConstants.RecordSessionKeyTargetPath, string.Empty);
            WriteSidecar(id, new RecordResultPayload
            {
                recordingId = id,
                status = "Interrupted",
                path = string.IsNullOrEmpty(basePath) ? string.Empty : basePath + ".mp4",
            });
            ClearState();
        }

        private static void RegisterAutoStopPoll()
        {
            void Poll()
            {
                if (!HasActiveRecording())
                {
                    EditorApplication.update -= Poll;
                    return;
                }

                double elapsed = (DateTime.UtcNow - _startedAtUtc).TotalSeconds;
                bool durationHit = _durationSeconds > 0 && elapsed >= _durationSeconds;
                bool safetyHit = elapsed >= ProtocolConstants.MaxRecordDurationSeconds;
                bool playExited = !EditorApplication.isPlaying;
                if (durationHit || safetyHit || playExited)
                {
                    EditorApplication.update -= Poll;
                    try
                    {
                        FinalizeAndBuildResult("Completed");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[UCB] record auto-stop failed: " + ex);
                        ClearState();
                    }
                }
            }

            EditorApplication.update += Poll;
        }

        private static string SidecarPath(string recordingId)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dir = Path.Combine(projectRoot, ProtocolConstants.RecordingsDirectoryRelative);
            return Path.Combine(dir, recordingId + ".json");
        }

        private static void WriteSidecar(string recordingId, RecordResultPayload payload)
        {
            AtomicFileUtility.WriteAllText(SidecarPath(recordingId), ProtocolJson.Serialize(payload));
        }

        private static bool HasActiveRecording()
        {
            lock (_activeLock)
            {
                return _hasActiveRecording;
            }
        }

        private static bool TryBeginRecording()
        {
            lock (_activeLock)
            {
                if (_hasActiveRecording)
                {
                    return false;
                }

                _hasActiveRecording = true;
                return true;
            }
        }
    }
}
