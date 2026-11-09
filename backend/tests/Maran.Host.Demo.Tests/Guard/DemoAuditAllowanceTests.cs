using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The guard cannot be made to fill the journal.</summary>
public sealed class DemoAuditAllowanceTests
{
    /// <summary>Only the configured number of refusals may be journaled.</summary>
    [Fact]
    public void Refusals_are_journaled_up_to_the_allowance_and_no_further()
    {
        var allowance = new DemoAuditAllowance(
            new OptionsWrapper<DemoOptions>(new DemoOptions { AuditedRefusalsPerWindow = 2 }));

        Assert.True(allowance.TryTakeRefusal());
        Assert.True(allowance.TryTakeRefusal());
        Assert.False(allowance.TryTakeRefusal());
    }

    /// <summary>The exhaustion row is taken once.</summary>
    [Fact]
    public void The_exhaustion_row_is_taken_once()
    {
        var allowance = new DemoAuditAllowance(new OptionsWrapper<DemoOptions>(new DemoOptions()));

        Assert.True(allowance.TryTakeExhaustion());
        Assert.False(allowance.TryTakeExhaustion());
    }

    /// <summary>An allowance of zero journals no refusal at all.</summary>
    [Fact]
    public void An_allowance_of_zero_journals_nothing()
    {
        var allowance = new DemoAuditAllowance(
            new OptionsWrapper<DemoOptions>(new DemoOptions { AuditedRefusalsPerWindow = 0 }));

        Assert.False(allowance.TryTakeRefusal());
    }
}
