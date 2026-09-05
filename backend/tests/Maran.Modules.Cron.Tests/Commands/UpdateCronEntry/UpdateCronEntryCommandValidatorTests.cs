using Maran.Modules.Cron.Commands.UpdateCronEntry;
using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Tests.Commands.UpdateCronEntry;

/// <summary>
/// What the operation that rewrites an entry accepts: the shared identifier, schedule and command
/// rules, each asserted here rather than assumed from the operation that removes one.
/// </summary>
/// <remarks>
/// Four validators bind <c>CronEntryIdRule</c>, and a rule that lives in four files has to stay
/// bound in four files. Asserting it only on the delete path would leave the other three free to
/// lose the line without a test noticing — the agent would still refuse the value, but the panel
/// would have become the layer that widened what the agent narrowed.
/// </remarks>
public sealed class UpdateCronEntryCommandValidatorTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly CronScheduleDto Nightly = new("0", "3", "*", "*", "*");

    /// <summary>A well formed update is accepted.</summary>
    [Fact]
    public void A_well_formed_update_is_accepted()
    {
        // The other side of every refusal below: a validator that says no to everything is not a
        // validator, it is an outage.
        Assert.True(IsValid("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d", Nightly, "/usr/bin/backup"));
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
        Assert.False(IsValid(entryId, Nightly, "/usr/bin/backup"));
    }

    /// <summary>A schedule the agent would refuse is refused here too.</summary>
    [Fact]
    public void A_schedule_the_agent_would_refuse_is_refused_here_too()
    {
        // The shared CronScheduleValidator is bound to this operation, not only to the creation:
        // an update rewrites the crontab line, so it can install a schedule a creation could not.
        Assert.False(IsValid(
            "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d",
            new CronScheduleDto("0", "3", "*", "*", "7"),
            "/usr/bin/backup"));
    }

    /// <summary>A command carrying a newline is refused because the entry file holds one line.</summary>
    [Fact]
    public void A_command_carrying_a_newline_is_refused_because_the_entry_file_holds_one_line()
    {
        Assert.False(IsValid(
            "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d",
            Nightly,
            "/usr/bin/backup\nMAILTO=attacker@example.test"));
    }

    /// <summary>Runs the validator over one update.</summary>
    /// <param name="entryId">The identifier to check.</param>
    /// <param name="schedule">The schedule to check.</param>
    /// <param name="command">The command line to check.</param>
    /// <returns>Whether the validator accepted the command.</returns>
    private static bool IsValid(string entryId, CronScheduleDto schedule, string command)
    {
        return new UpdateCronEntryCommandValidator()
            .Validate(new UpdateCronEntryCommand(AccountId, entryId, schedule, command, "203.0.113.7", "tests"))
            .IsValid;
    }
}
