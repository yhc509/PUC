using System.Net;
using System.Net.Sockets;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ClientDisconnectMonitorTests
{
    [Fact]
    public async Task WaitForDisconnectAsync_WithOpenConnection_WaitsUntilWriterCloses()
    {
        using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        using var reader = new StreamReader(streams.ServerStream);

        Task<bool> task = ClientDisconnectMonitor.WaitForDisconnectAsync(reader);

        Assert.False(await CompletesWithinAsync(task, TimeSpan.FromMilliseconds(100)));

        streams.ClientStream.Dispose();

        Assert.True(await CompletesWithinAsync(task, TimeSpan.FromSeconds(2)));
        Assert.True(await task);
    }

    [Fact]
    public async Task WaitForDisconnectAsync_WithImmediateEof_ReturnsTrue()
    {
        using var reader = new StringReader(string.Empty);

        bool disconnected = await ClientDisconnectMonitor.WaitForDisconnectAsync(reader);

        Assert.True(disconnected);
    }

    [Fact]
    public async Task WaitForDisconnectAsync_WithUnexpectedNextLine_ReturnsFalse()
    {
        using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        using var reader = new StreamReader(streams.ServerStream);
        using var writer = new StreamWriter(streams.ClientStream) { AutoFlush = true };

        Task<bool> task = ClientDisconnectMonitor.WaitForDisconnectAsync(reader);

        await writer.WriteLineAsync("unexpected");

        Assert.True(await CompletesWithinAsync(task, TimeSpan.FromSeconds(2)));
        Assert.False(await task);
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        return ReferenceEquals(completedTask, task);
    }

    private sealed class ConnectedStreams : IDisposable
    {
        private readonly TcpClient _client;
        private readonly TcpClient _server;

        private ConnectedStreams(TcpClient client, TcpClient server)
        {
            _client = client;
            _server = server;
            ClientStream = client.GetStream();
            ServerStream = server.GetStream();
        }

        public NetworkStream ClientStream { get; }
        public NetworkStream ServerStream { get; }

        public static async Task<ConnectedStreams> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var client = new TcpClient();
                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint).ConfigureAwait(false);
                TcpClient server = await acceptTask.ConfigureAwait(false);
                return new ConnectedStreams(client, server);
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Dispose()
        {
            ClientStream.Dispose();
            ServerStream.Dispose();
            _client.Dispose();
            _server.Dispose();
        }
    }
}
