#nullable enable
using System.IO;
using System.Threading.Tasks;

namespace UnityCli.Protocol
{
    /// <summary>
    /// Waits until a client closes a one-command-per-connection stream.
    /// After the command line has been read, a well-behaved client keeps the
    /// connection open while waiting for the response and sends no extra lines.
    /// </summary>
    public static class ClientDisconnectMonitor
    {
        /// <summary>
        /// Returns true when the client closes the connection (EOF/read failure).
        /// Returns false if an unexpected next line arrives instead.
        /// </summary>
        public static async Task<bool> WaitForDisconnectAsync(TextReader reader)
        {
            try
            {
                string? extra = await reader.ReadLineAsync().ConfigureAwait(false);
                return extra == null;
            }
            catch
            {
                return true;
            }
        }
    }
}
