#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityCli.Protocol
{
    public enum BoundedRequestReadStatus
    {
        /// <summary>A complete newline-terminated line was read within both budgets.</summary>
        Success,

        /// <summary>The line exceeded the byte budget before a newline arrived.</summary>
        ExceededSize,

        /// <summary>No complete line arrived before the read deadline elapsed.</summary>
        TimedOut,

        /// <summary>The stream reached EOF (or became unusable) before a newline arrived.</summary>
        Closed,
    }

    /// <summary>
    /// Outcome of a single bounded request read. Expected failures are reported
    /// through <see cref="Status"/> rather than thrown, so the caller can answer
    /// with an error envelope instead of tearing down the connection handler.
    /// </summary>
    public readonly struct BoundedRequestReadResult
    {
        private BoundedRequestReadResult(BoundedRequestReadStatus status, string? line, long bytesRead)
        {
            Status = status;
            Line = line;
            BytesRead = bytesRead;
        }

        public BoundedRequestReadStatus Status { get; }

        /// <summary>The decoded line without its terminator. Non-null only on <see cref="BoundedRequestReadStatus.Success"/>.</summary>
        public string? Line { get; }

        /// <summary>Raw bytes consumed from the stream, terminator included. Useful for diagnostics on every status.</summary>
        public long BytesRead { get; }

        public bool IsSuccess
        {
            get { return Status == BoundedRequestReadStatus.Success; }
        }

        public static BoundedRequestReadResult FromLine(string line, long bytesRead)
        {
            return new BoundedRequestReadResult(BoundedRequestReadStatus.Success, line, bytesRead);
        }

        public static BoundedRequestReadResult ExceededSize(long bytesRead)
        {
            return new BoundedRequestReadResult(BoundedRequestReadStatus.ExceededSize, null, bytesRead);
        }

        public static BoundedRequestReadResult TimedOut(long bytesRead)
        {
            return new BoundedRequestReadResult(BoundedRequestReadStatus.TimedOut, null, bytesRead);
        }

        public static BoundedRequestReadResult Closed(long bytesRead)
        {
            return new BoundedRequestReadResult(BoundedRequestReadStatus.Closed, null, bytesRead);
        }
    }

    /// <summary>
    /// Reads the single newline-terminated UTF-8 request line of a
    /// one-command-per-connection stream under a byte budget and a deadline.
    ///
    /// This is robustness, not a privilege boundary: the listening socket is
    /// already owner-only (0600), so anything on the other end runs as the same
    /// user. The budgets exist so a buggy or crashed client cannot make the
    /// Editor buffer an unbounded line, and cannot leave a handler task plus its
    /// socket alive for the rest of the Editor session by connecting and never
    /// writing.
    /// </summary>
    public static class BoundedRequestReader
    {
        /// <summary>
        /// 32 MiB. `execute --code` bodies and asset content travel on this same
        /// line, so the cap has to sit far above any real payload while still
        /// bounding how much heap one connection can make the Editor allocate.
        /// </summary>
        public const int MaxRequestBytes = 32 * 1024 * 1024;

        /// <summary>
        /// 30 s. The CLI writes its request immediately after connecting, so a
        /// legitimate client finishes in milliseconds; this only reclaims
        /// connections that opened and then went silent. It bounds the initial
        /// read alone — never how long the command itself may run.
        /// </summary>
        public const int ReadTimeoutMs = 30_000;

        private const int ReadChunkBytes = 8192;
        private const int TimeoutSentinel = -1;
        private const byte LineFeed = 10;
        private const byte CarriageReturn = 13;

        public static Task<BoundedRequestReadResult> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            return ReadLineAsync(stream, MaxRequestBytes, ReadTimeoutMs, cancellationToken);
        }

        /// <summary>
        /// Reads one line from <paramref name="stream"/>. Both "\n" and "\r\n"
        /// terminate a line; the terminator is stripped from the result.
        /// <paramref name="maxBytes"/> is the budget for the line content, so a
        /// line of exactly <paramref name="maxBytes"/> bytes still succeeds.
        /// Bytes trailing the terminator inside the final chunk are discarded —
        /// this is one request per connection and the stream is not resynced.
        /// External cancellation is reported as
        /// <see cref="BoundedRequestReadStatus.Closed"/>; the connection is going
        /// away either way and there is nothing left to answer.
        /// </summary>
        public static async Task<BoundedRequestReadResult> ReadLineAsync(
            Stream stream,
            int maxBytes,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
            }

            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "timeoutMs must be positive.");
            }

            // One byte of slack so a line of exactly maxBytes terminated by
            // "\r\n" is still recognised before the trailing '\r' is stripped.
            long rawLimit = (long)maxBytes + 1;

            var buffer = new byte[ReadChunkBytes];
            long totalRead = 0;
            var stopwatch = Stopwatch.StartNew();

            using (var accumulated = new MemoryStream())
            {
                while (true)
                {
                    int remainingMs = timeoutMs - (int)Math.Min(stopwatch.ElapsedMilliseconds, timeoutMs);
                    if (remainingMs <= 0)
                    {
                        return BoundedRequestReadResult.TimedOut(totalRead);
                    }

                    int read;
                    try
                    {
                        read = await ReadChunkAsync(stream, buffer, remainingMs, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return BoundedRequestReadResult.Closed(totalRead);
                    }

                    if (read == TimeoutSentinel)
                    {
                        return BoundedRequestReadResult.TimedOut(totalRead);
                    }

                    if (read <= 0)
                    {
                        // EOF before any terminator: either nothing was ever sent,
                        // or the client died mid-line. Neither is a usable request.
                        return BoundedRequestReadResult.Closed(totalRead);
                    }

                    totalRead += read;

                    int newlineIndex = IndexOf(buffer, read, LineFeed);
                    int usable = newlineIndex >= 0 ? newlineIndex : read;

                    if (accumulated.Length + usable > rawLimit)
                    {
                        return BoundedRequestReadResult.ExceededSize(totalRead);
                    }

                    accumulated.Write(buffer, 0, usable);

                    if (newlineIndex < 0)
                    {
                        continue;
                    }

                    // GetBuffer over ToArray: the accumulated line can reach
                    // MaxRequestBytes, and every extra copy of it lands on the
                    // large object heap of a process that runs for days.
                    byte[] content = accumulated.GetBuffer();
                    int length = (int)accumulated.Length;
                    if (length > 0 && content[length - 1] == CarriageReturn)
                    {
                        length--;
                    }

                    if (length > maxBytes)
                    {
                        return BoundedRequestReadResult.ExceededSize(totalRead);
                    }

                    return BoundedRequestReadResult.FromLine(
                        Encoding.UTF8.GetString(content, 0, length),
                        totalRead);
                }
            }
        }

        private static int IndexOf(byte[] buffer, int count, byte value)
        {
            for (int index = 0; index < count; index++)
            {
                if (buffer[index] == value)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Reads one chunk, giving up after <paramref name="timeoutMs"/>.
        /// The race is run with Task.Delay rather than by handing the token to
        /// the stream, because socket reads do not reliably honour a token on
        /// every runtime this file is compiled for. Returns
        /// <see cref="TimeoutSentinel"/> when the deadline wins.
        /// </summary>
        private static async Task<int> ReadChunkAsync(
            Stream stream,
            byte[] buffer,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            Task<int> readTask = stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (readTask.IsCompleted)
            {
                return await readTask.ConfigureAwait(false);
            }

            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task delayTask = Task.Delay(timeoutMs, delayCancellation.Token);
                Task winner = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
                if (ReferenceEquals(winner, readTask))
                {
                    delayCancellation.Cancel();
                    return await readTask.ConfigureAwait(false);
                }

                // The read is abandoned, not cancelled: it completes or faults
                // once the caller disposes the stream. Observe it so a fault
                // never surfaces as an unobserved task exception. This has to
                // happen before the throw below, because cancellation abandons
                // the read exactly the same way a timeout does.
                ObserveFailure(readTask);
                cancellationToken.ThrowIfCancellationRequested();
                return TimeoutSentinel;
            }
        }

        private static void ObserveFailure(Task task)
        {
            task.ContinueWith(
                completed => { _ = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
