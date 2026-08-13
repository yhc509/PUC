namespace UnityCli.Protocol
{
    /// <summary>
    /// What the listener watchdog wants the host to do on this tick.
    /// </summary>
    internal enum ListenerWatchdogDecision
    {
        /// <summary>Nothing to do — healthy, throttled, or already given up.</summary>
        None,

        /// <summary>The listener came back after a recovery attempt; re-publish the registry entry.</summary>
        Recovered,

        /// <summary>The listener is dead; try to bind it again in place.</summary>
        Restart,

        /// <summary>Recovery attempts are exhausted; stop advertising this instance as live.</summary>
        Abandon,
    }

    /// <summary>
    /// Decision logic for the bridge's listener watchdog, kept free of Unity types so it is
    /// unit-testable. The accept loops can die on an unexpected exception without any retry,
    /// while the registry heartbeat keeps advertising the instance — a zombie the CLI routes to
    /// and then fails to connect to. This policy drives a bounded in-place rebind and, once the
    /// attempts are spent, tells the host to stop advertising instead.
    /// </summary>
    internal sealed class ListenerWatchdogPolicy
    {
        private readonly double _checkIntervalSeconds;
        private readonly int _maxRecoveryAttempts;
        private double _lastCheckSeconds;
        private int _recoveryAttempts;
        private bool _hasAbandoned;

        internal ListenerWatchdogPolicy(double checkIntervalSeconds, int maxRecoveryAttempts, double startedAtSeconds)
        {
            _checkIntervalSeconds = checkIntervalSeconds;
            _maxRecoveryAttempts = maxRecoveryAttempts;
            _lastCheckSeconds = startedAtSeconds;
        }

        /// <summary>Recovery attempts spent so far; reset once the listener is healthy again.</summary>
        internal int RecoveryAttempts
        {
            get { return _recoveryAttempts; }
        }

        /// <summary>True once the policy has stopped trying to recover.</summary>
        internal bool HasAbandoned
        {
            get { return _hasAbandoned; }
        }

        /// <param name="isListenerReady">The listener is bound and accepting.</param>
        /// <param name="isListenerStarting">A bind attempt is still in flight — never race it.</param>
        /// <param name="isEditorBusy">Compiling/updating; the listener teardown is expected then.</param>
        /// <param name="nowSeconds">Monotonic-ish clock, same source across calls.</param>
        internal ListenerWatchdogDecision Evaluate(
            bool isListenerReady,
            bool isListenerStarting,
            bool isEditorBusy,
            double nowSeconds)
        {
            if (_hasAbandoned)
            {
                // The host has already stopped advertising this instance; there is nothing left to
                // report, and a listener cannot come back because nothing is retrying it.
                return ListenerWatchdogDecision.None;
            }

            if (isListenerReady)
            {
                _lastCheckSeconds = nowSeconds;
                if (_recoveryAttempts == 0)
                {
                    return ListenerWatchdogDecision.None;
                }

                _recoveryAttempts = 0;
                return ListenerWatchdogDecision.Recovered;
            }

            if (isListenerStarting || isEditorBusy)
            {
                // A bind in flight or a domain reload in progress resets the clock: the interval
                // measures time spent visibly dead, not time spent waiting for a legitimate start.
                _lastCheckSeconds = nowSeconds;
                return ListenerWatchdogDecision.None;
            }

            if (nowSeconds - _lastCheckSeconds < _checkIntervalSeconds)
            {
                return ListenerWatchdogDecision.None;
            }

            _lastCheckSeconds = nowSeconds;
            if (_recoveryAttempts >= _maxRecoveryAttempts)
            {
                _hasAbandoned = true;
                return ListenerWatchdogDecision.Abandon;
            }

            _recoveryAttempts++;
            return ListenerWatchdogDecision.Restart;
        }
    }
}
