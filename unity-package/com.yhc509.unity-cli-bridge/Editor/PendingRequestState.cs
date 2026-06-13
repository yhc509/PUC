#nullable enable
using System.Threading;

namespace UnityCli.Protocol
{
    public enum PendingRequestCancelReason
    {
        None,
        ClientDisconnected,
        HostShutdown,
    }

    /// <summary>
    /// Atomic single-claim lifecycle for a queued IPC request. Exactly one of
    /// dispatch / client-disconnect-cancel / host-shutdown-cancel can win the
    /// transition from Queued; the losers observe failure. Closes the
    /// dispatch-vs-cancel race so a disconnected request is never both
    /// cancelled and executed (#78).
    /// </summary>
    public sealed class PendingRequestState
    {
        private const int Queued = 0;
        private const int Dispatching = 1;
        private const int CancelledDisconnect = 2;
        private const int CancelledShutdown = 3;

        private int _state = Queued;

        /// <summary>Drain claims the request for execution. False if already cancelled/claimed.</summary>
        public bool TryClaimForDispatch()
        {
            return Interlocked.CompareExchange(ref _state, Dispatching, Queued) == Queued;
        }

        /// <summary>Watcher cancels because the client disconnected before dispatch. False if already claimed/cancelled.</summary>
        public bool TryCancelForClientDisconnect()
        {
            return Interlocked.CompareExchange(ref _state, CancelledDisconnect, Queued) == Queued;
        }

        /// <summary>Host shutdown cancels the request. False if already claimed/cancelled.</summary>
        public bool TryCancelForHostShutdown()
        {
            return Interlocked.CompareExchange(ref _state, CancelledShutdown, Queued) == Queued;
        }

        public PendingRequestCancelReason CancelReason
        {
            get
            {
                switch (Volatile.Read(ref _state))
                {
                    case CancelledDisconnect:
                        return PendingRequestCancelReason.ClientDisconnected;
                    case CancelledShutdown:
                        return PendingRequestCancelReason.HostShutdown;
                    default:
                        return PendingRequestCancelReason.None;
                }
            }
        }
    }
}
