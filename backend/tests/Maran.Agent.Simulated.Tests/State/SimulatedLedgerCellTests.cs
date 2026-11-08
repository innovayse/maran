using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Tests.State;

/// <summary>A single thread-safe slot for a fact a host has once.</summary>
public sealed class SimulatedLedgerCellTests
{
    /// <summary>An empty slot answers with the fallback the caller names, and never with a made-up value.</summary>
    [Fact]
    public void An_empty_cell_is_null_and_answers_with_the_fallback()
    {
        var cell = new SimulatedLedgerCell<string>(new object(), null);

        Assert.Null(cell.Value);
        Assert.Equal("fallback", cell.ValueOr("fallback"));
    }

    /// <summary>A replaced value is what every later read sees.</summary>
    [Fact]
    public void A_replaced_value_is_what_later_reads_see()
    {
        var cell = new SimulatedLedgerCell<string>(new object(), "first");

        cell.Replace("second");

        Assert.Equal("second", cell.Value);
        Assert.Equal("second", cell.ValueOr("fallback"));
    }
}
