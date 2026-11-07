using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the gate that keeps two walks along the chain from running at once. Every
/// test that holds a walk open is paired with a test where nothing is held, so a gate that blocked
/// everything would fail the second.
/// </summary>
public sealed class AuditChainVerificationGateTests : IDisposable
{
    private readonly AuditChainVerificationGate _gate = new();

    /// <summary>Releases the gate this test owns.</summary>
    public void Dispose()
    {
        _gate.Dispose();
    }

    private static AuditChainVerdict Verdict(long rows)
    {
        return new AuditChainVerdict(true, rows, null, AuditChainBreak.None);
    }

    /// <summary>With nothing running, a call runs its own walk and returns its answer (the inverse control).</summary>
    [Fact]
    public async Task With_nothing_running_a_call_runs_its_own_walk()
    {
        var answer = Verdict(1);

        var result = await _gate.RunAsync(_ =>
        {
            return Task.FromResult(answer);
        }, CancellationToken.None);

        Assert.Same(answer, result);
    }

    /// <summary>Two calls in turn each run their own walk, because nothing was running when the second arrived.</summary>
    [Fact]
    public async Task Calls_that_do_not_overlap_each_run_their_own_walk()
    {
        var runs = 0;

        var first = await _gate.RunAsync(_ =>
        {
            return Task.FromResult(Verdict(++runs));
        }, CancellationToken.None);
        var second = await _gate.RunAsync(_ =>
        {
            return Task.FromResult(Verdict(++runs));
        }, CancellationToken.None);

        Assert.Equal(2, runs);
        Assert.NotSame(first, second);
    }

    /// <summary>A second call does not start its walk until the first has finished.</summary>
    [Fact]
    public async Task A_second_call_waits_for_the_walk_in_progress()
    {
        var release = new TaskCompletionSource<AuditChainVerdict>();
        var secondStarted = false;

        var first = _gate.RunAsync(_ =>
        {
            return release.Task;
        }, CancellationToken.None);
        var second = _gate.RunAsync(
            _ =>
            {
                secondStarted = true;

                return Task.FromResult(Verdict(2));
            },
            CancellationToken.None);

        Assert.False(secondStarted);

        release.SetResult(Verdict(1));
        await first;
        await second;

        Assert.True(secondStarted);
    }

    /// <summary>
    /// Two calls that arrive while one walk runs share the answer of the next walk: one of them runs it, and
    /// the other takes it, because that walk started after both had arrived.
    /// </summary>
    [Fact]
    public async Task Callers_who_arrived_during_a_walk_share_the_next_walk()
    {
        var release = new TaskCompletionSource<AuditChainVerdict>();
        var walks = 0;

        var running = _gate.RunAsync(_ =>
        {
            return release.Task;
        }, CancellationToken.None);
        var waiterOne = _gate.RunAsync(_ =>
        {
            return Task.FromResult(Verdict(++walks));
        }, CancellationToken.None);
        var waiterTwo = _gate.RunAsync(_ =>
        {
            return Task.FromResult(Verdict(++walks));
        }, CancellationToken.None);

        release.SetResult(Verdict(100));
        var started = await running;
        var one = await waiterOne;
        var two = await waiterTwo;

        Assert.Equal(1, walks);
        Assert.Same(one, two);
        Assert.NotSame(started, one);
    }

    /// <summary>A walk that failed releases the gate, and the next call runs normally.</summary>
    [Fact]
    public async Task A_failed_walk_releases_the_gate()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
            {
                return _gate.RunAsync(_ =>
                {
                    throw new InvalidOperationException("the database went away");
                }, CancellationToken.None);
            });

        var result = await _gate.RunAsync(_ =>
        {
            return Task.FromResult(Verdict(3));
        }, CancellationToken.None);

        Assert.Equal(3, result.RowsChecked);
    }

    /// <summary>A caller who stops waiting leaves the walk in progress alone.</summary>
    [Fact]
    public async Task A_caller_who_stops_waiting_does_not_disturb_the_walk_in_progress()
    {
        var release = new TaskCompletionSource<AuditChainVerdict>();
        using var cancel = new CancellationTokenSource();

        var running = _gate.RunAsync(_ =>
        {
            return release.Task;
        }, CancellationToken.None);
        var impatient = _gate.RunAsync(_ =>
        {
            return Task.FromResult(Verdict(2));
        }, cancel.Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return impatient;
        });

        release.SetResult(Verdict(1));

        Assert.Equal(1, (await running).RowsChecked);
    }
}
