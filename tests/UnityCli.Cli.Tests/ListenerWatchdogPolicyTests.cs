using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ListenerWatchdogPolicyTests
{
    private const double Interval = 5.0;
    private const int MaxAttempts = 3;

    private static ListenerWatchdogPolicy CreatePolicy()
    {
        return new ListenerWatchdogPolicy(Interval, MaxAttempts, 0.0);
    }

    private static ListenerWatchdogDecision Healthy(ListenerWatchdogPolicy policy, double now)
    {
        return policy.Evaluate(isListenerReady: true, isListenerStarting: false, isEditorBusy: false, nowSeconds: now);
    }

    private static ListenerWatchdogDecision Dead(ListenerWatchdogPolicy policy, double now)
    {
        return policy.Evaluate(isListenerReady: false, isListenerStarting: false, isEditorBusy: false, nowSeconds: now);
    }

    [Fact]
    public void Evaluate_WhenListenerHealthy_StaysQuiet()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        Assert.Equal(ListenerWatchdogDecision.None, Healthy(policy, 1.0));
        Assert.Equal(ListenerWatchdogDecision.None, Healthy(policy, 100.0));
        Assert.Equal(0, policy.RecoveryAttempts);
    }

    [Fact]
    public void Evaluate_WhenListenerDiesButIntervalHasNotElapsed_Waits()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        Assert.Equal(ListenerWatchdogDecision.None, Dead(policy, 4.9));
    }

    [Fact]
    public void Evaluate_WhenListenerStaysDeadPastInterval_RequestsRestart()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        Assert.Equal(ListenerWatchdogDecision.Restart, Dead(policy, 5.0));
        Assert.Equal(1, policy.RecoveryAttempts);
    }

    [Fact]
    public void Evaluate_WhileBindIsInFlight_DoesNotRaceIt()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        Assert.Equal(ListenerWatchdogDecision.Restart, Dead(policy, 5.0));

        // The restart is running: not ready yet, but not dead either.
        for (double now = 5.1; now <= 30.0; now += 1.0)
        {
            Assert.Equal(
                ListenerWatchdogDecision.None,
                policy.Evaluate(isListenerReady: false, isListenerStarting: true, isEditorBusy: false, nowSeconds: now));
        }

        Assert.Equal(1, policy.RecoveryAttempts);
    }

    [Fact]
    public void Evaluate_WhileEditorIsBusy_DoesNotFightDomainReload()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        for (double now = 1.0; now <= 60.0; now += 1.0)
        {
            Assert.Equal(
                ListenerWatchdogDecision.None,
                policy.Evaluate(isListenerReady: false, isListenerStarting: false, isEditorBusy: true, nowSeconds: now));
        }

        Assert.Equal(0, policy.RecoveryAttempts);
    }

    [Fact]
    public void Evaluate_WhenListenerComesBack_ReportsRecoveredOnceAndResetsAttempts()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        Assert.Equal(ListenerWatchdogDecision.Restart, Dead(policy, 5.0));
        Assert.Equal(ListenerWatchdogDecision.Recovered, Healthy(policy, 6.0));
        Assert.Equal(0, policy.RecoveryAttempts);
        Assert.Equal(ListenerWatchdogDecision.None, Healthy(policy, 7.0));
    }

    [Fact]
    public void Evaluate_AfterRecovery_AllowsTheFullAttemptBudgetAgain()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        Assert.Equal(ListenerWatchdogDecision.Restart, Dead(policy, 5.0));
        Assert.Equal(ListenerWatchdogDecision.Recovered, Healthy(policy, 6.0));

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Assert.Equal(ListenerWatchdogDecision.Restart, Dead(policy, 6.0 + (attempt * Interval)));
        }

        Assert.False(policy.HasAbandoned);
    }

    [Fact]
    public void Evaluate_WhenRecoveryAttemptsAreExhausted_AbandonsExactlyOnce()
    {
        ListenerWatchdogPolicy policy = CreatePolicy();

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Assert.Equal(ListenerWatchdogDecision.Restart, Dead(policy, attempt * Interval));
        }

        Assert.Equal(ListenerWatchdogDecision.Abandon, Dead(policy, (MaxAttempts + 1) * Interval));
        Assert.True(policy.HasAbandoned);

        // Never advertises again, and never re-reports the abandon.
        Assert.Equal(ListenerWatchdogDecision.None, Dead(policy, (MaxAttempts + 5) * Interval));
        Assert.Equal(ListenerWatchdogDecision.None, Healthy(policy, (MaxAttempts + 6) * Interval));
    }
}
