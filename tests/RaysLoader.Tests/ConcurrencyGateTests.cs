using RaysLoader.Services;

namespace RaysLoader.Tests;

public sealed class ConcurrencyGateTests
{
    [Fact]
    public async Task LoweringLimitDelaysNewSlotsUntilEnoughAreReleased()
    {
        var gate = new ConcurrencyGate(2);
        await gate.WaitAsync(CancellationToken.None);
        await gate.WaitAsync(CancellationToken.None);
        gate.SetLimit(1);

        var third = gate.WaitAsync(CancellationToken.None);
        gate.Release();
        await Task.Delay(50);
        Assert.False(third.IsCompleted);

        gate.Release();
        await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, gate.InUse);
    }

    [Fact]
    public async Task WaitingCanBeCancelled()
    {
        var gate = new ConcurrencyGate(1);
        await gate.WaitAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var waiting = gate.WaitAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(1, gate.InUse);
    }
}
