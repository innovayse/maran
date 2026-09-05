using Maran.Modules.Cron.Commands.SetCronEntryEnabled;

namespace Maran.Modules.Cron.Tests.Commands.SetCronEntryEnabled;

/// <summary>
/// What the operation that switches an entry on or off accepts: the shared identifier rule, and no
/// rule at all on the flag.
/// </summary>
public sealed class SetCronEntryEnabledCommandValidatorTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>Both states of the flag are legal requests.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_states_of_the_flag_are_legal_requests(bool enabled)
    {
        // A boolean has two values and both are things a customer may ask for; the validator exists
        // for the identifier beside it, not for the flag.
        Assert.True(IsValid("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d", enabled));
    }

    /// <summary>An identifier shaped like a path is refused before it can reach the agent.</summary>
    [Theory]
    [InlineData("../../../etc/cron.d/evil")]
    [InlineData("/etc/cron.d/evil")]
    [InlineData("3F1A5B7C-0D2E-4A6B-8C9D-0E1F2A3B4C5D")]
    [InlineData("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d\n")]
    [InlineData("")]
    public void An_identifier_shaped_like_a_path_is_refused_before_it_can_reach_the_agent(string entryId)
    {
        Assert.False(IsValid(entryId, enabled: true));
    }

    /// <summary>Runs the validator over one switch.</summary>
    /// <param name="entryId">The identifier to check.</param>
    /// <param name="enabled">The state the entry is to be put in.</param>
    /// <returns>Whether the validator accepted the command.</returns>
    private static bool IsValid(string entryId, bool enabled)
    {
        return new SetCronEntryEnabledCommandValidator()
            .Validate(new SetCronEntryEnabledCommand(AccountId, entryId, enabled, "203.0.113.7", "tests"))
            .IsValid;
    }
}
