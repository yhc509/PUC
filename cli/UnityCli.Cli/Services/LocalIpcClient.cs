using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

public sealed class LocalIpcClient
{
    public async Task<ResponseEnvelope> SendAsync(
        InstanceRecord target,
        CommandEnvelope command,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        return OperatingSystem.IsWindows()
            ? await SendNamedPipeAsync(target.pipeName, command, timeoutMs, cancellationToken)
            : await SendUnixSocketAsync(target.pipeName, command, timeoutMs, cancellationToken);
    }

    private static async Task<ResponseEnvelope> SendNamedPipeAsync(
        string pipeName,
        CommandEnvelope command,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        await using var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeoutMs);
        await stream.ConnectAsync(linkedCts.Token);
        return await ExchangeAsync(stream, command, linkedCts.Token);
    }

    private static async Task<ResponseEnvelope> SendUnixSocketAsync(
        string socketPath,
        CommandEnvelope command,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeoutMs);
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        await socket.ConnectAsync(endpoint, linkedCts.Token);
        await using var stream = new NetworkStream(socket, ownsSocket: true);
        return await ExchangeAsync(stream, command, linkedCts.Token);
    }

    private static async Task<ResponseEnvelope> ExchangeAsync(Stream stream, CommandEnvelope command, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        command.protocolVersion = ProtocolConstants.ProtocolVersion;
        await writer.WriteLineAsync(ProtocolJson.Serialize(command));
        await writer.FlushAsync(cancellationToken);

        var responseLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new IOException("Unity IPC 응답이 비어 있습니다.");
        }

        var response = ProtocolJson.Deserialize<ResponseEnvelope>(responseLine);
        if (response is null)
        {
            throw new IOException("Unity IPC 응답을 파싱하지 못했습니다.");
        }

        return EnsureCompatibleResponse(response);
    }

    internal static ResponseEnvelope EnsureCompatibleResponse(ResponseEnvelope response)
    {
        if (string.Equals(response.protocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
        {
            return response;
        }

        return ResponseEnvelope.Failure(
            string.IsNullOrWhiteSpace(response.requestId) ? Guid.NewGuid().ToString("N") : response.requestId,
            response.target,
            ProtocolConstants.ErrorProtocolMismatch,
            "Unity package version is incompatible with this CLI. Please upgrade both CLI binary and Unity package together.",
            false,
            response.durationMs,
            string.IsNullOrWhiteSpace(response.transport) ? ProtocolConstants.TransportLive : response.transport,
            "Expected protocolVersion " + ProtocolConstants.ProtocolVersion + ".");
    }
}
