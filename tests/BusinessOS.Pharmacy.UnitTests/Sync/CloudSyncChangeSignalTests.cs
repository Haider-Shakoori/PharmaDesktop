using BusinessOS.Pharmacy.Sync;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Sync;

public sealed class CloudSyncChangeSignalTests
{
    [Fact]
    public async Task Notify_wakes_waiter_without_waiting_for_interval()
    {
        using var signal = new CloudSyncChangeSignal();
        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Repeated_notifications_are_coalesced()
    {
        using var signal = new CloudSyncChangeSignal();
        signal.Notify();
        signal.Notify();
        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task Wait_without_changes_times_out()
    {
        using var signal = new CloudSyncChangeSignal();
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20)));
    }
}
