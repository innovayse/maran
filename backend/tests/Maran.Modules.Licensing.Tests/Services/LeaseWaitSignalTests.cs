using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LeaseWaitSignal"/>: a pass that ran out of turn ends the schedule's wait and tells the loop what
/// it amounted to, one pass ends one wait, a pass that ran while nobody waited ends nothing, and a wait nobody interrupts runs out. The delays that must not elapse
/// are an hour, so a test that wrongly waited for one would not finish; the delay that must elapse is a few
/// milliseconds.
/// </summary>
public sealed class LeaseWaitSignalTests
{
    /// <summary>A delay no test waits out.</summary>
    private static readonly TimeSpan LongDelay = TimeSpan.FromHours(1);

    /// <summary>A delay a test does wait out.</summary>
    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>A pass that ran during the wait ends it at once and hands over its outcome.</summary>
    [Fact]
    public async Task A_pass_that_ran_during_the_wait_ends_it_with_its_outcome()
    {
        var signal = new LeaseWaitSignal();
        var waiting = signal.WaitAsync(LongDelay, CancellationToken.None);

        signal.Notify(LeaseRenewalOutcome.Unreachable);

        Assert.Equal(LeaseRenewalOutcome.Unreachable, await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>A wait nobody interrupts runs out and reports no outcome; the case above is its control.</summary>
    [Fact]
    public async Task A_wait_nobody_interrupts_runs_out_and_reports_no_outcome()
    {
        var signal = new LeaseWaitSignal();

        Assert.Null(await signal.WaitAsync(ShortDelay, CancellationToken.None));
    }

    /// <summary>
    /// A pass that completed while nobody was waiting is not remembered: it would end the loop's next wait with an
    /// outcome the loop's own pass did not earn.
    /// </summary>
    [Fact]
    public async Task A_pass_that_completed_before_the_wait_began_does_not_end_it()
    {
        var signal = new LeaseWaitSignal();
        signal.Notify(LeaseRenewalOutcome.Renewed);

        Assert.Null(await signal.WaitAsync(ShortDelay, CancellationToken.None));
    }

    /// <summary>One pass ends one wait: the next wait is not ended by a pass already taken.</summary>
    [Fact]
    public async Task One_pass_ends_one_wait_only()
    {
        var signal = new LeaseWaitSignal();
        var first = signal.WaitAsync(LongDelay, CancellationToken.None);
        signal.Notify(LeaseRenewalOutcome.Renewed);
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(await signal.WaitAsync(ShortDelay, CancellationToken.None));
    }

    /// <summary>A pass that completes after the wait ran out is not carried into the next wait.</summary>
    [Fact]
    public async Task A_pass_that_completes_after_a_wait_ran_out_does_not_end_the_next_one()
    {
        var signal = new LeaseWaitSignal();
        await signal.WaitAsync(ShortDelay, CancellationToken.None);
        signal.Notify(LeaseRenewalOutcome.Unreachable);

        Assert.Null(await signal.WaitAsync(ShortDelay, CancellationToken.None));
    }

    /// <summary>A host that is shutting down ends the wait with the cancellation the loop ends on, not with an outcome.</summary>
    [Fact]
    public async Task A_stopping_host_ends_the_wait_with_a_cancellation()
    {
        var signal = new LeaseWaitSignal();
        using var stopping = new CancellationTokenSource();
        var waiting = signal.WaitAsync(LongDelay, stopping.Token);

        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => { return waiting; });
    }
}
