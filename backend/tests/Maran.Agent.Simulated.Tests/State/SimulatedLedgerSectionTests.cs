using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Tests.State;

/// <summary>A keyed, bounded table that is safe to share between requests.</summary>
public sealed class SimulatedLedgerSectionTests
{
    /// <summary>A new key is stored; the same key again is a conflict and changes nothing.</summary>
    [Fact]
    public void Adding_a_key_twice_reports_already_exists_and_keeps_the_first_entry()
    {
        var section = NewSection(10);

        var first = section.Add("a", "one");
        var second = section.Add("a", "two");

        Assert.Equal(SimulatedLedgerAdd.Added, first);
        Assert.Equal(SimulatedLedgerAdd.AlreadyExists, second);
        Assert.True(section.TryGet("a", out var kept));
        Assert.Equal("one", kept);
    }

    /// <summary>The section never holds more entries than its cap.</summary>
    [Fact]
    public void Adding_past_the_cap_reports_limit_reached_and_stores_nothing()
    {
        var section = NewSection(2);
        section.Add("a", "1");
        section.Add("b", "2");

        var third = section.Add("c", "3");

        Assert.Equal(SimulatedLedgerAdd.LimitReached, third);
        Assert.Equal(2, section.Count);
        Assert.False(section.Contains("c"));
    }

    /// <summary>A key that is removed frees its slot under the cap.</summary>
    [Fact]
    public void Removing_an_entry_frees_its_slot()
    {
        var section = NewSection(1);
        section.Add("a", "1");

        Assert.True(section.Remove("a"));
        Assert.False(section.Remove("a"));
        Assert.Equal(SimulatedLedgerAdd.Added, section.Add("b", "2"));
    }

    /// <summary>Replacing builds the new entry from the old one and reports whether there was one.</summary>
    [Fact]
    public void Replacing_builds_the_new_entry_from_the_old_one()
    {
        var section = NewSection(10);
        section.Add("a", "one");

        var replaced = section.Replace("a", current => { return current + "-two"; });
        var missing = section.Replace("b", current => { return current + "-two"; });

        Assert.True(replaced);
        Assert.False(missing);
        Assert.True(section.TryGet("a", out var entry));
        Assert.Equal("one-two", entry);
    }

    /// <summary>A snapshot is ordered by key and is not affected by later changes.</summary>
    [Fact]
    public void A_snapshot_is_ordered_by_key_and_does_not_change_afterwards()
    {
        var section = NewSection(10);
        section.Add("b", "2");
        section.Add("a", "1");
        section.Add("c", "3");

        var snapshot = section.Snapshot();
        section.Remove("a");

        string[] expected = ["1", "2", "3"];
        Assert.Equal(expected, snapshot);
        Assert.Equal(2, section.Count);
    }

    /// <summary>The cap holds when many threads add at once.</summary>
    [Fact]
    public void The_cap_holds_under_concurrent_adds()
    {
        var section = NewSection(50);

        Parallel.For(
            0,
            1000,
            index => { section.Add("key-" + index, "value"); });

        Assert.Equal(50, section.Count);
    }

    /// <summary>A section needs a lock to share and a positive cap.</summary>
    [Fact]
    public void A_section_without_a_lock_or_with_a_zero_cap_is_a_defect()
    {
        Assert.Throws<ArgumentNullException>(() => { return new SimulatedLedgerSection<string>(null!, 1); });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { return new SimulatedLedgerSection<string>(new object(), 0); });
    }

    /// <summary>Builds an empty section with its own lock.</summary>
    /// <param name="maxEntries">The cap.</param>
    /// <returns>The section.</returns>
    private static SimulatedLedgerSection<string> NewSection(int maxEntries)
    {
        return new SimulatedLedgerSection<string>(new object(), maxEntries);
    }
}
