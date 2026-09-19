using Maran.Modules.Accounts.Commands.RepairAccountHomeGroups;

namespace Maran.Modules.Accounts.Tests.Commands.RepairAccountHomeGroups;

/// <summary>The one rule the repair's confirmation carries, and why it is not the handler's gate.</summary>
public sealed class RepairAccountHomeGroupsCommandValidatorTests
{
    /// <summary>A negative confirmation is refused as malformed input.</summary>
    /// <remarks>
    /// No report ever produces a negative figure, so this is a malformed request rather than a stale
    /// plan. Answering it as a stale plan would send the operator to re-read a report that was never
    /// the problem.
    /// </remarks>
    [Fact]
    public void A_negative_confirmation_is_refused_as_malformed_input()
    {
        var result = new RepairAccountHomeGroupsCommandValidator()
            .Validate(new RepairAccountHomeGroupsCommand(-1));

        Assert.False(result.IsValid);
        Assert.Equal("AccountHomeGroupRepairCountInvalid", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Zero and a positive confirmation are both accepted here.</summary>
    /// <remarks>
    /// The inverse control rules/testing.md requires: a validator mutated to refuse everything would
    /// pass the case above while making the repair unreachable. Zero is included deliberately — it is
    /// the figure a clean host's report gives.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Zero_and_a_positive_confirmation_are_both_accepted_here(int confirmed)
    {
        var result = new RepairAccountHomeGroupsCommandValidator()
            .Validate(new RepairAccountHomeGroupsCommand(confirmed));

        Assert.True(result.IsValid);
    }
}
