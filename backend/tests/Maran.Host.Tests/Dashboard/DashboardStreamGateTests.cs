using Maran.Host.Configuration;
using Maran.Host.Dashboard;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Dashboard;

/// <summary>The process-wide cap on open dashboard streams: admits up to the limit, refuses past it, frees on release.</summary>
public sealed class DashboardStreamGateTests
{
    /// <summary>A gate admits streams up to its limit and refuses the next.</summary>
    [Fact]
    public void A_gate_admits_streams_up_to_its_limit_and_refuses_the_next()
    {
        var gate = GateOf(2);

        using var first = gate.TryEnter();
        using var second = gate.TryEnter();
        var third = gate.TryEnter();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(third);
        Assert.Equal(2, gate.Open);
    }

    /// <summary>Releasing a slot lets the next stream in.</summary>
    [Fact]
    public void Releasing_a_slot_lets_the_next_stream_in()
    {
        var gate = GateOf(1);
        var held = gate.TryEnter();
        Assert.Null(gate.TryEnter());

        held!.Dispose();

        using var next = gate.TryEnter();
        Assert.NotNull(next);
    }

    /// <summary>A slot disposed twice is returned once, so a stream cannot widen the limit by ending twice.</summary>
    [Fact]
    public void A_slot_disposed_twice_is_returned_once()
    {
        var gate = GateOf(1);
        var held = gate.TryEnter();

        held!.Dispose();
        held.Dispose();

        using var only = gate.TryEnter();
        Assert.NotNull(only);
        Assert.Null(gate.TryEnter());
        Assert.Equal(1, gate.Open);
    }

    /// <summary>Many streams racing for the last slots never get more than the limit.</summary>
    [Fact]
    public async Task Many_streams_racing_for_the_last_slots_never_get_more_than_the_limit()
    {
        var gate = GateOf(5);

        var leases = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ =>
        {
            return Task.Run(gate.TryEnter);
        }));

        Assert.Equal(5, leases.Count(lease =>
        {
            return lease is not null;
        }));
        Assert.Equal(5, gate.Open);
    }

    /// <summary>Builds a gate with a given limit.</summary>
    /// <param name="limit">The most streams it admits.</param>
    /// <returns>The gate.</returns>
    private static DashboardStreamGate GateOf(int limit)
    {
        return new DashboardStreamGate(
            new OptionsWrapper<DashboardStreamOptions>(new DashboardStreamOptions { PerProcessStreamLimit = limit }));
    }
}
