using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class PendingRequestStateTests
{
    [Fact]
    public void TryClaimForDispatch_WhenFirst_BlocksLaterDisconnectCancel()
    {
        var state = new PendingRequestState();

        Assert.True(state.TryClaimForDispatch());
        Assert.False(state.TryCancelForClientDisconnect());
        Assert.Equal(PendingRequestCancelReason.None, state.CancelReason);
    }

    [Fact]
    public void TryCancelForClientDisconnect_WhenFirst_BlocksLaterDispatch()
    {
        var state = new PendingRequestState();

        Assert.True(state.TryCancelForClientDisconnect());
        Assert.False(state.TryClaimForDispatch());
        Assert.Equal(PendingRequestCancelReason.ClientDisconnected, state.CancelReason);
    }

    [Fact]
    public void TryCancelForHostShutdown_WhenFirst_BlocksLaterClaims()
    {
        var state = new PendingRequestState();

        Assert.True(state.TryCancelForHostShutdown());
        Assert.False(state.TryCancelForClientDisconnect());
        Assert.False(state.TryClaimForDispatch());
        Assert.Equal(PendingRequestCancelReason.HostShutdown, state.CancelReason);
    }

    [Fact]
    public async Task ConcurrentClaims_AllowExactlyOneWinner()
    {
        for (int iteration = 0; iteration < 250; iteration++)
        {
            var state = new PendingRequestState();
            using var start = new ManualResetEventSlim(false);
            int[] successes = { 0 };

            Task[] tasks =
            {
                Task.Run(() => TryClaim(start, state.TryClaimForDispatch, successes)),
                Task.Run(() => TryClaim(start, state.TryClaimForDispatch, successes)),
                Task.Run(() => TryClaim(start, state.TryCancelForClientDisconnect, successes)),
                Task.Run(() => TryClaim(start, state.TryCancelForClientDisconnect, successes)),
                Task.Run(() => TryClaim(start, state.TryCancelForHostShutdown, successes)),
                Task.Run(() => TryClaim(start, state.TryCancelForHostShutdown, successes)),
            };

            start.Set();
            await Task.WhenAll(tasks);

            Assert.Equal(1, successes[0]);
        }
    }

    private static void TryClaim(ManualResetEventSlim start, Func<bool> operation, int[] successes)
    {
        start.Wait();
        if (operation())
        {
            Interlocked.Increment(ref successes[0]);
        }
    }
}
