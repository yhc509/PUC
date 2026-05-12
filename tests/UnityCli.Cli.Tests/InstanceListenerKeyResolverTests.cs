using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class InstanceListenerKeyResolverTests
{
    [Fact]
    public void TryAcquire_FirstAttemptSucceeds_ReturnsBaseHash()
    {
        var attempts = new List<string>();
        var listener = InstanceListenerKeyResolver.Acquire<object>(
            "abc123def456",
            maxAttempts: 16,
            tryBindWithHash: hash =>
            {
                attempts.Add(hash);
                return new object();
            },
            out var acquiredHash);

        Assert.NotNull(listener);
        Assert.Equal("abc123def456", acquiredHash);
        Assert.Single(attempts);
    }

    [Fact]
    public void TryAcquire_FirstThreeFail_ReturnsSuffixedHash()
    {
        var attempts = new List<string>();
        var listener = InstanceListenerKeyResolver.Acquire<object>(
            "abc123def456",
            maxAttempts: 16,
            tryBindWithHash: hash =>
            {
                attempts.Add(hash);
                return attempts.Count >= 4 ? new object() : null;
            },
            out var acquiredHash);

        Assert.NotNull(listener);
        Assert.Equal("abc123def456-3", acquiredHash);
        Assert.Equal(["abc123def456", "abc123def456-1", "abc123def456-2", "abc123def456-3"], attempts);
    }

    [Fact]
    public void TryAcquire_AllAttemptsFail_ThrowsInvalidOperation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            InstanceListenerKeyResolver.Acquire<object>(
                "abc123def456",
                maxAttempts: 4,
                tryBindWithHash: _ => null,
                out _));

        Assert.Contains("abc123def456", exception.Message);
        Assert.Contains("4", exception.Message);
    }
}
