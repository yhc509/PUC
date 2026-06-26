#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityCli.Protocol;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    [InitializeOnLoad]
    internal static class BridgeBootstrap
    {
        private static readonly BridgeHost _host;

        static BridgeBootstrap()
        {
            _host = new BridgeHost();
            _host.Start();
        }
    }

    internal sealed class BridgeHost : IDisposable
    {
        private static readonly UTF8Encoding _utf8WithoutBomEncoding = new UTF8Encoding(false);
        private static readonly IComparer<InstanceRecord> _instanceRecordComparer = new InstanceRecordProjectNameComparer();
        private readonly ConcurrentQueue<PendingRequest> _pendingRequests = new ConcurrentQueue<PendingRequest>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly string[] _capabilities;
        private readonly string _projectRoot;
        private readonly string _projectName;
        private readonly string _baseProjectHash;
        private readonly string _registryFilePath;
        private readonly AssetCommandHandler _assetCommandHandler;
        private readonly SceneCommandHandler _sceneCommandHandler;
        private readonly PrefabCommandHandler _prefabCommandHandler;
        private readonly ScreenshotCommandHandler _screenshotCommandHandler;
        private readonly ExecuteCodeHandler _executeCodeHandler;
        private readonly CustomCommandHandler _customCommandHandler;
        private readonly PackageCommandHandler _packageCommandHandler;
        private readonly TestCommandHandler _testCommandHandler;
        private readonly MaterialCommandHandler _materialCommandHandler;
        private readonly QaCommandHandler _qaCommandHandler;
        private readonly RecordCommandHandler _recordCommandHandler;
        private NamedPipeOwnershipLock? _namedPipeOwnershipLock;
#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
        private Socket? _unixListener;
#endif
        private double _lastHeartbeatTime;
        private bool _originalRunInBackground;
        private bool _isStarted;
        private bool _isDisposed;
        private bool _isInstanceRegistered;
        private volatile bool _isListenerReady;
        private string _projectHash = string.Empty;
        private string _pipeName = string.Empty;
        private const int ListenerAcquireMaxAttempts = 16;
        private const int NamedPipeMaxServerInstances = 2;
        private const int NamedPipeProbeTimeoutMilliseconds = 50;

        public BridgeHost()
        {
            _projectRoot = ProtocolConstants.GetCanonicalPath(Path.Combine(Application.dataPath, ".."));
            _projectName = Path.GetFileName(_projectRoot);
            _baseProjectHash = ProtocolConstants.ComputeProjectHash(_projectRoot);
            _projectHash = _baseProjectHash;
            _registryFilePath = RegistryPathUtility.GetRegistryFilePath();
            _capabilities = ProtocolHelpers.GetSupportedCommands();
            _assetCommandHandler = new AssetCommandHandler();
            _sceneCommandHandler = new SceneCommandHandler();
            _prefabCommandHandler = new PrefabCommandHandler();
            _screenshotCommandHandler = new ScreenshotCommandHandler();
            _executeCodeHandler = new ExecuteCodeHandler();
            _customCommandHandler = new CustomCommandHandler();
            _packageCommandHandler = new PackageCommandHandler();
            _testCommandHandler = new TestCommandHandler();
            _materialCommandHandler = new MaterialCommandHandler();
            _qaCommandHandler = new QaCommandHandler();
            _recordCommandHandler = new RecordCommandHandler();

            TestCommandHandler.RestoreLockFromSession();
            DomainReloadDisableScope.RestoreIfOrphaned();
        }

        public void Start()
        {
            if (_isStarted || Application.isBatchMode)
            {
                return;
            }

            _isStarted = true;
            ConsoleLogBuffer.Start();
            _lastHeartbeatTime = EditorApplication.timeSinceStartup;
            StartListener();

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.quitting -= OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch
            {
            }

#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
            DisposeUnixListener();
#endif
            ConsoleLogBuffer.Stop();
            RemoveInstance();
            CleanupSocketFile();
            DisposeNamedPipeOwnershipLock();
            _cancellationTokenSource.Dispose();
        }

        private void StartListener()
        {
#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
            // Unity 6+ / non-Unity: use raw Unix domain sockets for non-Windows.
            if (Path.DirectorySeparatorChar != '\\')
            {
                StartUnixSocketListener();
                return;
            }
#endif
            // Unity 2021-2023 (Mono): NamedPipeServerStream on non-Windows creates a Unix
            // domain socket at the exact _pipeName path, so the CLI's UnixDomainSocketEndPoint
            // connection works transparently. Verified on Unity 2021.3.57f2 + macOS (Mono).
            StartNamedPipeListener();
        }

        private async void StartNamedPipeListener()
        {
            try
            {
                await RunNamedPipeLoopAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportBackgroundException("named pipe listener", exception);
            }
        }

#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
        private async void StartUnixSocketListener()
        {
            try
            {
                await RunUnixSocketLoopAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportBackgroundException("unix socket listener", exception);
            }
        }
#endif

        private async Task RunNamedPipeLoopAsync(CancellationToken cancellationToken)
        {
            NamedPipeServerStream initialServer;
            try
            {
                initialServer = await Task.Run(delegate
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return InstanceListenerKeyResolver.Acquire<NamedPipeServerStream>(
                        _baseProjectHash,
                        ListenerAcquireMaxAttempts,
                        delegate(string hash)
                        {
                            string candidatePipeName = ProtocolConstants.BuildPipeName(hash);
                            NamedPipeOwnershipLock? ownershipLock = null;

                            try
                            {
                                if (Path.DirectorySeparatorChar == '\\'
                                    && !NamedPipeOwnershipLock.TryAcquire(candidatePipeName, out ownershipLock))
                                {
                                    return null;
                                }

                                if (IsLiveNamedPipe(candidatePipeName))
                                {
                                    return null;
                                }

                                var server = new NamedPipeServerStream(
                                    candidatePipeName,
                                    PipeDirection.InOut,
                                    NamedPipeMaxServerInstances,
                                    PipeTransmissionMode.Byte,
                                    PipeOptions.Asynchronous);
                                _projectHash = hash;
                                _pipeName = candidatePipeName;
                                _namedPipeOwnershipLock = ownershipLock;
                                ownershipLock = null;
                                return server;
                            }
                            catch (IOException)
                            {
                                return null;
                            }
                            catch (UnauthorizedAccessException)
                            {
                                return null;
                            }
                            finally
                            {
                                if (ownershipLock != null)
                                {
                                    ownershipLock.Dispose();
                                }
                            }
                        },
                        out _);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                ReportBackgroundException("named pipe listener", exception);
                return;
            }

            NamedPipeServerStream? server = initialServer;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (server == null)
                        {
                            server = new NamedPipeServerStream(
                                _pipeName,
                                PipeDirection.InOut,
                                NamedPipeMaxServerInstances,
                                PipeTransmissionMode.Byte,
                                PipeOptions.Asynchronous);
                        }

                        _isListenerReady = true;
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        _ = HandleNamedPipeClientAsync(server, cancellationToken);
                        server = null;
                    }
                    catch (OperationCanceledException)
                    {
                        server?.Dispose();
                        server = null;
                    }
                    catch (Exception exception)
                    {
                        ReportBackgroundException("named pipe accept", exception);
                        server?.Dispose();
                        server = null;
                    }
                }
            }
            finally
            {
                server?.Dispose();
                DisposeNamedPipeOwnershipLock();
            }
        }

        private static bool IsLiveNamedPipe(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return false;
            }

            using (var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                try
                {
                    client.Connect(NamedPipeProbeTimeoutMilliseconds);
                    return true;
                }
                catch (TimeoutException)
                {
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }

#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
        private async Task RunUnixSocketLoopAsync(CancellationToken cancellationToken)
        {
            Socket listener;

            try
            {
                listener = await Task.Run(delegate
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return InstanceListenerKeyResolver.Acquire<Socket>(
                        _baseProjectHash,
                        ListenerAcquireMaxAttempts,
                        delegate(string hash)
                        {
                            string candidatePipeName = ProtocolConstants.BuildPipeName(hash);
                            if (!UnixSocketFileUtility.TryCleanupDeadSocketFile(candidatePipeName))
                            {
                                return null;
                            }

                            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                            try
                            {
                                socket.Bind(new UnixDomainSocketEndPoint(candidatePipeName));
                                socket.Listen(8);
                                _projectHash = hash;
                                _pipeName = candidatePipeName;
                                return socket;
                            }
                            catch (SocketException)
                            {
                                socket.Dispose();
                                return null;
                            }
                            catch (IOException)
                            {
                                socket.Dispose();
                                return null;
                            }
                        },
                        out _);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                ReportBackgroundException("unix socket listener", exception);
                return;
            }

            _unixListener = listener;
            _isListenerReady = true;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Socket client = await listener.AcceptAsync().ConfigureAwait(false);
                    HandleSocketClient(client, cancellationToken);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                ReportBackgroundException("unix socket accept", exception);
            }
            finally
            {
                if (ReferenceEquals(_unixListener, listener))
                {
                    _unixListener = null;
                }

                listener.Dispose();
                CleanupSocketFile();
            }
        }
#endif

        private async Task HandleNamedPipeClientAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            try
            {
                await HandleStreamClientAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportBackgroundException("named pipe client", exception);
            }
        }

#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
        private async void HandleSocketClient(Socket client, CancellationToken cancellationToken)
        {
            try
            {
                await HandleSocketClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                }

                ReportBackgroundException("unix socket client", exception);
            }
        }

        private Task HandleSocketClientAsync(Socket client, CancellationToken cancellationToken)
        {
            return HandleStreamClientAsync(new NetworkStream(client, true), cancellationToken);
        }
#endif

        private async Task HandleStreamClientAsync(Stream stream, CancellationToken cancellationToken)
        {
            using (stream)
            using (var writer = new StreamWriter(stream, _utf8WithoutBomEncoding, 1024, true))
            using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
            {
                string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                CommandEnvelope? command;
                try
                {
                    command = ProtocolJson.Deserialize<CommandEnvelope>(line);
                }
                catch (Exception deserializeException)
                {
                    var error = ResponseEnvelope.Failure(
                        Guid.NewGuid().ToString("N"),
                        _projectHash,
                        "INVALID_COMMAND",
                        "command payload를 해석하지 못했습니다: " + deserializeException.Message,
                        false,
                        0,
                        ProtocolConstants.TransportLive,
                        ProtocolErrorDetails.FromString(line));
                    await WriteResponseAsync(writer, error);
                    return;
                }

                if (command == null || string.IsNullOrWhiteSpace(command.command))
                {
                    string requestId = command != null && !string.IsNullOrWhiteSpace(command.requestId)
                        ? command.requestId
                        : Guid.NewGuid().ToString("N");
                    var error = ResponseEnvelope.Failure(
                        requestId,
                        _projectHash,
                        "INVALID_COMMAND",
                        "command payload를 해석하지 못했습니다.",
                        false,
                        0,
                        ProtocolConstants.TransportLive,
                        ProtocolErrorDetails.FromString(line));
                    await WriteResponseAsync(writer, error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(command.requestId))
                {
                    command.requestId = Guid.NewGuid().ToString("N");
                }

                if (!string.Equals(command.protocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
                {
                    var error = ResponseEnvelope.Failure(
                        command.requestId,
                        _projectHash,
                        ProtocolConstants.ErrorProtocolMismatch,
                        "CLI version is incompatible with this Unity package. Please upgrade both CLI binary and Unity package together.",
                        false,
                        0,
                        ProtocolConstants.TransportLive,
                        ProtocolErrorDetails.FromString("Expected protocolVersion " + ProtocolConstants.ProtocolVersion + "."));
                    await WriteResponseAsync(writer, error);
                    return;
                }

                var pending = new PendingRequest(command);
                _pendingRequests.Enqueue(pending);

                using (cancellationToken.Register(CancelPendingRequest, pending))
                {
                    _ = MonitorClientDisconnectAsync(reader, pending);

                    ResponseEnvelope response;
                    try
                    {
                        response = await pending.Completion.Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        if (pending.State.CancelReason == PendingRequestCancelReason.ClientDisconnected)
                        {
                            return;
                        }

                        response = ResponseEnvelope.Failure(
                            command.requestId,
                            _projectHash,
                            "REQUEST_CANCELLED",
                            "요청 처리 중 브리지가 종료되었습니다.",
                            true,
                            0,
                            ProtocolConstants.TransportLive,
                            null);
                    }

                    await WriteResponseAsync(writer, response).ConfigureAwait(false);
                }
            }
        }

        private static void CancelPendingRequest(object state)
        {
            PendingRequest pending = state as PendingRequest;
            if (pending == null)
            {
                return;
            }

            if (pending.State.TryCancelForHostShutdown())
            {
                pending.Completion.TrySetCanceled();
            }
        }

        private static async Task MonitorClientDisconnectAsync(TextReader reader, PendingRequest pending)
        {
            bool disconnected = await ClientDisconnectMonitor.WaitForDisconnectAsync(reader).ConfigureAwait(false);
            if (disconnected && pending.State.TryCancelForClientDisconnect())
            {
                pending.Completion.TrySetCanceled();
            }
        }

        private static async Task WriteResponseAsync(StreamWriter writer, ResponseEnvelope response)
        {
            string responseJson;
            try
            {
                responseJson = EnvelopeJsonWriter.Write(response);
            }
            catch (Exception exception)
            {
                responseJson = EnvelopeJsonWriter.Write(ResponseEnvelope.Failure(
                    response.requestId,
                    response.target,
                    ProtocolConstants.ErrorInternalInvalidPayload,
                    "Bridge generated an invalid response payload.",
                    false,
                    response.durationMs,
                    response.transport,
                    ProtocolErrorDetails.FromString(exception.ToString())));
            }

            await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        private void OnEditorUpdate()
        {
            if (_isDisposed)
            {
                return;
            }

            if (!_isInstanceRegistered && _isListenerReady)
            {
                RegisterInstance();
                _isInstanceRegistered = true;
                _lastHeartbeatTime = EditorApplication.timeSinceStartup;
            }
            else if (_isInstanceRegistered
                && EditorApplication.timeSinceStartup - _lastHeartbeatTime >= ProtocolConstants.RegistryHeartbeatSeconds)
            {
                RegisterInstance();
                _lastHeartbeatTime = EditorApplication.timeSinceStartup;
            }

            while (_pendingRequests.TryDequeue(out PendingRequest pending))
            {
                if (!pending.State.TryClaimForDispatch())
                {
                    continue;
                }

                if (_qaCommandHandler.CanHandle(pending.Command.command) && _qaCommandHandler.IsDeferred(pending.Command.command, pending.Command.argumentsJson))
                {
                    StartDeferredQaRequest(pending);
                    continue;
                }

                if (_testCommandHandler.CanHandle(pending.Command.command) && _testCommandHandler.IsDeferred(pending.Command.command, pending.Command.argumentsJson))
                {
                    StartDeferredTestRequest(pending);
                    continue;
                }

                if (_packageCommandHandler.CanHandle(pending.Command.command) && _packageCommandHandler.IsDeferred(pending.Command.command, pending.Command.argumentsJson))
                {
                    StartDeferredPackageRequest(pending);
                    continue;
                }

                ResponseEnvelope response = HandleCommand(pending.Command);
                pending.Completion.TrySetResult(response);
            }
        }

        private void StartDeferredTestRequest(PendingRequest pending)
        {
            CommandEnvelope command = pending.Command;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (IsBusyEditorCommand(command.command))
                {
                    stopwatch.Stop();
                    pending.Completion.TrySetResult(BuildBusyResponse(command, stopwatch.ElapsedMilliseconds));
                    return;
                }

                _testCommandHandler.StartDeferred(command.command, command.argumentsJson, pending.Completion, _projectHash);
            }
            catch (CommandFailureException exception)
            {
                if (string.Equals(command.command, ProtocolConstants.CommandTestRun, StringComparison.Ordinal))
                {
                    TestCommandHandler.EndRun();
                }

                stopwatch.Stop();
                pending.Completion.TrySetResult(ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    exception.ErrorCode,
                    exception.Message,
                    exception.IsRetryable,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    exception.Details));
            }
            catch (Exception exception)
            {
                if (string.Equals(command.command, ProtocolConstants.CommandTestRun, StringComparison.Ordinal))
                {
                    TestCommandHandler.EndRun();
                }

                stopwatch.Stop();
                pending.Completion.TrySetResult(ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    "TEST_RUN_START_FAILED",
                    exception.Message,
                    false,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    ProtocolErrorDetails.FromString(exception.ToString())));
            }
        }

        private void StartDeferredQaRequest(PendingRequest pending)
        {
            CommandEnvelope command = pending.Command;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (IsBusyEditorCommand(command.command))
                {
                    stopwatch.Stop();
                    pending.Completion.TrySetResult(BuildBusyResponse(command, stopwatch.ElapsedMilliseconds));
                    return;
                }

                _qaCommandHandler.StartDeferred(command.command, command.argumentsJson, pending.Completion, _projectHash);
            }
            catch (CommandFailureException exception)
            {
                stopwatch.Stop();
                pending.Completion.TrySetResult(ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    exception.ErrorCode,
                    exception.Message,
                    exception.IsRetryable,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    exception.Details));
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                pending.Completion.TrySetResult(ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    "COMMAND_FAILED",
                    exception.Message,
                    false,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    ProtocolErrorDetails.FromString(exception.ToString())));
            }
        }

        private void StartDeferredPackageRequest(PendingRequest pending)
        {
            CommandEnvelope command = pending.Command;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (IsBusyEditorCommand(command.command))
                {
                    stopwatch.Stop();
                    pending.Completion.TrySetResult(BuildBusyResponse(command, stopwatch.ElapsedMilliseconds));
                    return;
                }

                _packageCommandHandler.StartDeferred(command.command, command.argumentsJson, pending.Completion, _projectHash);
            }
            catch (CommandFailureException exception)
            {
                stopwatch.Stop();
                pending.Completion.TrySetResult(ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    exception.ErrorCode,
                    exception.Message,
                    exception.IsRetryable,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    exception.Details));
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                pending.Completion.TrySetResult(ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    "COMMAND_FAILED",
                    exception.Message,
                    false,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    ProtocolErrorDetails.FromString(exception.ToString())));
            }
        }

        private void OnEditorQuitting()
        {
            Dispose();
        }

        private void OnBeforeAssemblyReload()
        {
            Dispose();
        }

        private ResponseEnvelope HandleCommand(CommandEnvelope command)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (IsBusyEditorCommand(command.command))
                {
                    stopwatch.Stop();
                    return BuildBusyResponse(command, stopwatch.ElapsedMilliseconds);
                }

                string data;
                if (_assetCommandHandler.CanHandle(command.command))
                {
                    data = _assetCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_sceneCommandHandler.CanHandle(command.command))
                {
                    data = _sceneCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_prefabCommandHandler.CanHandle(command.command))
                {
                    data = _prefabCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_screenshotCommandHandler.CanHandle(command.command))
                {
                    data = _screenshotCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_executeCodeHandler.CanHandle(command.command))
                {
                    data = _executeCodeHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_customCommandHandler.CanHandle(command.command))
                {
                    data = _customCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_materialCommandHandler.CanHandle(command.command))
                {
                    data = _materialCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_qaCommandHandler.CanHandle(command.command))
                {
                    data = _qaCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_testCommandHandler.CanHandle(command.command))
                {
                    data = _testCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_packageCommandHandler.CanHandle(command.command))
                {
                    data = _packageCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else if (_recordCommandHandler.CanHandle(command.command))
                {
                    data = _recordCommandHandler.Handle(command.command, command.argumentsJson);
                }
                else
                {
                    switch (command.command)
                    {
                        case ProtocolConstants.CommandPing:
                            data = ProtocolJson.Serialize(new PingPayload
                            {
                                message = "pong",
                                timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                            });
                            break;
                        case ProtocolConstants.CommandStatus:
                            data = BuildStatusJson();
                            break;
                        case ProtocolConstants.CommandRefresh:
                            AssetDatabase.Refresh();
                            data = ProtocolJson.Serialize(new MessagePayload { message = "AssetDatabase.Refresh 완료" });
                            break;
                        case ProtocolConstants.CommandCompile:
                            CompilationPipeline.RequestScriptCompilation();
                            data = ProtocolJson.Serialize(new MessagePayload { message = "script compilation 요청 완료" });
                            break;
                        case ProtocolConstants.CommandPlay:
                            EditorApplication.isPaused = false;
                            EditorApplication.isPlaying = true;
                            _originalRunInBackground = UnityEngine.Application.runInBackground;
                            UnityEngine.Application.runInBackground = true;
                            data = ProtocolJson.Serialize(new PlayStatePayload { isPlaying = true });
                            break;
                        case ProtocolConstants.CommandPause:
                            EditorApplication.isPaused = true;
                            data = ProtocolJson.Serialize(new PauseStatePayload { isPaused = true });
                            break;
                        case ProtocolConstants.CommandStop:
                            EditorApplication.isPlaying = false;
                            EditorApplication.isPaused = false;
                            UnityEngine.Application.runInBackground = _originalRunInBackground;
                            data = ProtocolJson.Serialize(new StopStatePayload
                            {
                                isPlaying = false,
                                isPaused = false,
                            });
                            break;
                        case ProtocolConstants.CommandExecuteMenu:
                            data = HandleExecuteMenu(command.argumentsJson);
                            break;
                        case ProtocolConstants.CommandReadConsole:
                            data = HandleReadConsole(command.argumentsJson);
                            break;
                        default:
                            throw new InvalidOperationException("지원하지 않는 명령입니다: " + command.command);
                    }
                }

                stopwatch.Stop();
                if (string.Equals(command.command, ProtocolConstants.CommandTestResults, StringComparison.Ordinal))
                {
                    return TestCommandHandler.BuildTestRunResultEnvelope(
                        command.requestId,
                        _projectHash,
                        data,
                        stopwatch.ElapsedMilliseconds);
                }

                return ResponseEnvelope.Success(
                    command.requestId,
                    _projectHash,
                    data,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive);
            }
            catch (CommandFailureException exception)
            {
                stopwatch.Stop();
                return ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    exception.ErrorCode,
                    exception.Message,
                    exception.IsRetryable,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    exception.Details);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return ResponseEnvelope.Failure(
                    command.requestId,
                    _projectHash,
                    "COMMAND_FAILED",
                    exception.Message,
                    false,
                    stopwatch.ElapsedMilliseconds,
                    ProtocolConstants.TransportLive,
                    ProtocolErrorDetails.FromString(exception.ToString()));
            }
        }

        private bool IsBusyEditorCommand(string command)
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                return false;
            }

            return !ProtocolHelpers.IsCommandAllowedWhileBusy(command);
        }

        private ResponseEnvelope BuildBusyResponse(CommandEnvelope command, long durationMs)
        {
            var busyDetails = "잠시(약 5~10초) 후 재시도하세요. 새 Unity Editor 인스턴스를 띄우지 마세요.\n" +
                              "진단: `unity-cli instances list`로 현재 인스턴스를 확인하고, " +
                              "`unity-cli read-console --type error`로 컴파일 에러를 확인하세요.";

            return ResponseEnvelope.Failure(
                command.requestId,
                _projectHash,
                ProtocolConstants.BusyErrorCode,
                "Unity가 compile/update 중이라 지금 명령을 처리할 수 없습니다.",
                true,
                durationMs,
                ProtocolConstants.TransportLive,
                ProtocolErrorDetails.FromString(busyDetails));
        }

        private string BuildStatusJson()
        {
            return ProtocolJson.Serialize(new StatusPayload
            {
                projectRoot = _projectRoot,
                projectHash = _projectHash,
                projectName = _projectName,
                unityVersion = Application.unityVersion,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                activeScenePath = EditorSceneManager.GetActiveScene().path,
                pipeName = _pipeName,
            });
        }

        private string HandleExecuteMenu(string argumentsJson)
        {
            ExecuteMenuArgs args = ProtocolJson.Deserialize<ExecuteMenuArgs>(argumentsJson) ?? new ExecuteMenuArgs();
            if (args.list)
            {
                if (!string.IsNullOrWhiteSpace(args.path))
                {
                    throw new CommandFailureException("INVALID_ARGS", "execute-menu에서는 path와 list를 동시에 지정할 수 없습니다.");
                }

                return ListMenuItems(args.prefix);
            }

            if (string.IsNullOrWhiteSpace(args.path))
            {
                throw new CommandFailureException("INVALID_ARGS", "execute-menu에는 path가 필요합니다.");
            }

            bool isExecuted = EditorApplication.ExecuteMenuItem(args.path);
            return ProtocolJson.Serialize(new ExecuteMenuPayload
            {
                path = args.path,
                executed = isExecuted,
                menus = Array.Empty<string>(),
            });
        }

        private static string ListMenuItems(string? prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new CommandFailureException("INVALID_ARGS", "execute-menu --list에는 prefix가 필요합니다.");
            }

            Type? unsupportedType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Unsupported");
            if (unsupportedType == null)
            {
                throw new CommandFailureException("MENU_LIST_UNAVAILABLE", "UnityEditor.Unsupported 타입을 찾지 못했습니다.");
            }

            MethodInfo? getSubmenus = unsupportedType.GetMethod(
                "GetSubmenus",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (getSubmenus is null)
            {
                throw new CommandFailureException("MENU_LIST_UNAVAILABLE", "UnityEditor.Unsupported.GetSubmenus API를 찾지 못했습니다.");
            }

            string[] menus = getSubmenus.Invoke(null, new object[] { prefix }) as string[] ?? Array.Empty<string>();
            return ProtocolJson.Serialize(new ExecuteMenuPayload
            {
                prefix = prefix,
                menus = menus,
            });
        }

        private string HandleReadConsole(string argumentsJson)
        {
            ReadConsoleArgs args = ProtocolJson.Deserialize<ReadConsoleArgs>(argumentsJson) ?? new ReadConsoleArgs();
            int limit = args.limit <= 0 ? ProtocolConstants.DefaultConsoleLimit : args.limit;
            ConsoleLogEntry[] entries = ConsoleLogBuffer.Read(limit, args.type);
            if (ConsoleLogProjectionUtility.ShouldOmitStackTrace(args))
            {
                return JsonConvert.SerializeObject(new
                {
                    entries = ConsoleLogProjectionUtility.ApplyNoStackTrace(entries),
                }, BridgeJsonSettings.CamelCaseIgnoreNull);
            }

            return ProtocolJson.Serialize(new ReadConsolePayload { entries = entries });
        }

        private void RegisterInstance()
        {
            UpdateRegistrySafely(delegate(InstanceRegistry registry)
            {
                InstanceRecord[] existingRecords = registry.instances ?? Array.Empty<InstanceRecord>();
                var updatedRecords = new InstanceRecord[existingRecords.Length + 1];
                int updatedCount = 0;
                for (int i = 0; i < existingRecords.Length; i++)
                {
                    InstanceRecord record = existingRecords[i];
                    if (string.Equals(record.projectRoot, _projectRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    updatedRecords[updatedCount] = record;
                    updatedCount++;
                }

                updatedRecords[updatedCount] = BuildInstanceRecord();
                updatedCount++;
                if (updatedCount != updatedRecords.Length)
                {
                    Array.Resize(ref updatedRecords, updatedCount);
                }

                Array.Sort(updatedRecords, 0, updatedRecords.Length, _instanceRecordComparer);
                registry.instances = updatedRecords;

                InstanceRecord? activeRecord = null;
                if (!string.IsNullOrWhiteSpace(registry.activeProjectRoot))
                {
                    for (int i = 0; i < registry.instances.Length; i++)
                    {
                        InstanceRecord record = registry.instances[i];
                        if (string.Equals(record.projectRoot, registry.activeProjectRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            activeRecord = record;
                            break;
                        }
                    }
                }

                bool isCurrentProjectPromotionNeeded = activeRecord == null
                    || string.Equals(activeRecord.state, "offline", StringComparison.OrdinalIgnoreCase)
                    || activeRecord.editorProcessId <= 0;

                if (!registry.activeProjectRootPinned && isCurrentProjectPromotionNeeded)
                {
                    registry.activeProjectRoot = _projectRoot;
                }

                registry.activeProjectHash = null;
                return registry;
            });
        }

        private void RemoveInstance()
        {
            UpdateRegistrySafely(delegate(InstanceRegistry registry)
            {
                InstanceRecord[] existingRecords = registry.instances ?? Array.Empty<InstanceRecord>();
                var remainingRecords = new InstanceRecord[existingRecords.Length];
                int remainingCount = 0;
                for (int i = 0; i < existingRecords.Length; i++)
                {
                    InstanceRecord record = existingRecords[i];
                    if (string.Equals(record.projectRoot, _projectRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    remainingRecords[remainingCount] = record;
                    remainingCount++;
                }

                if (remainingCount != remainingRecords.Length)
                {
                    Array.Resize(ref remainingRecords, remainingCount);
                }

                registry.instances = remainingRecords;

                if (!registry.activeProjectRootPinned
                    && string.Equals(registry.activeProjectRoot, _projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    registry.activeProjectRoot = registry.instances.Length > 0 ? registry.instances[0].projectRoot : string.Empty;
                }

                registry.activeProjectHash = null;
                return registry;
            });
        }

        private InstanceRecord BuildInstanceRecord()
        {
            return new InstanceRecord
            {
                projectRoot = _projectRoot,
                projectName = _projectName,
                projectHash = _projectHash,
                pipeName = _pipeName,
                editorProcessId = Process.GetCurrentProcess().Id,
                unityVersion = Application.unityVersion,
                state = BuildStateLabel(),
                lastSeenUtc = DateTimeOffset.UtcNow.ToString("O"),
                capabilities = (string[])_capabilities.Clone(),
            };
        }

        private string BuildStateLabel()
        {
            if (EditorApplication.isCompiling)
            {
                return "compiling";
            }

            if (EditorApplication.isUpdating)
            {
                return "updating";
            }

            if (EditorApplication.isPlaying)
            {
                return "playing";
            }

            return "idle";
        }

        private void UpdateRegistrySafely(Func<InstanceRegistry, InstanceRegistry> update)
        {
            try
            {
                InstanceRegistryFile.Update(_registryFilePath, update);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(string.Format("Unity CLI bridge registry 갱신 실패: {0}", exception));
            }
        }

        private void CleanupSocketFile()
        {
            TryCleanupSocketFile(_pipeName);
        }

        private static void TryCleanupSocketFile(string pipeName)
        {
#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
            UnixSocketFileUtility.TryCleanupDeadSocketFile(pipeName, LogLiveSocketCleanupSkipped);
#else
            if (Path.DirectorySeparatorChar != '\\' && !string.IsNullOrWhiteSpace(pipeName) && File.Exists(pipeName))
            {
                try
                {
                    File.Delete(pipeName);
                }
                catch
                {
                }
            }
#endif
        }

        private static void LogLiveSocketCleanupSkipped(string pipeName)
        {
            UnityEngine.Debug.LogWarning(
                string.Format("Unity CLI bridge skipped cleanup for live Unix socket: {0}", pipeName));
        }

        private void DisposeNamedPipeOwnershipLock()
        {
            var ownershipLock = _namedPipeOwnershipLock;
            if (ownershipLock == null)
            {
                return;
            }

            _namedPipeOwnershipLock = null;
            ownershipLock.Dispose();
        }

#if !UNITY_5_3_OR_NEWER || UNITY_6000_0_OR_NEWER
        private void DisposeUnixListener()
        {
            if (_unixListener == null)
            {
                return;
            }

            try
            {
                _unixListener.Dispose();
            }
            catch
            {
            }

            _unixListener = null;
        }
#endif

        private void ReportBackgroundException(string operation, Exception exception)
        {
            if (_isDisposed || exception is OperationCanceledException)
            {
                return;
            }

            UnityEngine.Debug.LogWarning(string.Format("Unity CLI bridge {0} 실패: {1}", operation, exception));
        }

        private sealed class PendingRequest
        {
            public PendingRequest(CommandEnvelope command)
            {
                Command = command;
                State = new PendingRequestState();
                Completion = new TaskCompletionSource<ResponseEnvelope>(
                    command.requestId,
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public CommandEnvelope Command { get; private set; }
            public PendingRequestState State { get; private set; }
            public TaskCompletionSource<ResponseEnvelope> Completion { get; private set; }
        }

        private sealed class InstanceRecordProjectNameComparer : IComparer<InstanceRecord>
        {
            public int Compare(InstanceRecord x, InstanceRecord y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                return StringComparer.OrdinalIgnoreCase.Compare(x.projectName, y.projectName);
            }
        }
    }
}
