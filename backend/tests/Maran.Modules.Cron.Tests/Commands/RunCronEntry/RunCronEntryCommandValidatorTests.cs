using Maran.Modules.Cron.Commands.RunCronEntry;

namespace Maran.Modules.Cron.Tests.Commands.RunCronEntry;

/// <summary>What the run-now operation accepts: an account and an entry id of the agent's shape.</summary>
public sealed class RunCronEntryCommandValidatorTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>An agent-shaped entry id under an account is accepted.</summary>
    [Fact]
    public void An_agent_shaped_entry_id_under_an_account_is_accepted()
    {
        Assert.True(IsValid(AccountId, "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d"));
    }

    /// <summary>An identifier shaped like a path is refused before it can reach the agent.</summary>
    [Theory]
    [InlineData("../../../etc/cron.d/evil")]
    [InlineData("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d\n")]
    [InlineData("")]
    public void An_identifier_shaped_like_a_path_is_refused(string entryId)
    {
        Assert.False(IsValid(AccountId, entryId));
    }

    /// <summary>A run that names no account is refused.</summary>
    [Fact]
    public void A_run_that_names_no_account_is_refused()
    {
        Assert.False(IsValid(Guid.Empty, "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d"));
    }

    /// <summary>Runs the validator over one run request.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="entryId">The identifier to check.</param>
    /// <returns>Whether the validator accepted the command.</returns>
    private static bool IsValid(Guid accountId, string entryId)
    {
        return new RunCronEntryCommandValidator()
            .Validate(new RunCronEntryCommand(accountId, entryId))
            .IsValid;
    }
}
