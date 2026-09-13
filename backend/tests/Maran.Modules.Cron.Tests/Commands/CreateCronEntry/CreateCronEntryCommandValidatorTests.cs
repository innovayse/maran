using Maran.Modules.Cron.Commands.CreateCronEntry;
using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Tests.Commands.CreateCronEntry;

/// <summary>What a creation is refused for before it reaches its handler, and with which code.</summary>
public sealed class CreateCronEntryCommandValidatorTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>A valid creation passes.</summary>
    [Fact]
    public void A_valid_creation_passes()
    {
        Assert.Empty(Validate(Command()));
    }

    /// <summary>A malformed schedule is refused with the schedule code.</summary>
    [Fact]
    public void A_malformed_schedule_is_refused_with_the_schedule_code()
    {
        // The message IS the error code: the Host forwards a validation message only when it is
        // entirely alphanumeric and then resolves it against this module's resources, so an English
        // sentence here would be discarded and the customer would read a generic failure.
        var failures = Validate(Command() with { Schedule = new CronScheduleDto("99", "3", "*", "*", "*") });

        Assert.Contains("CronScheduleInvalid", failures);
    }

    /// <summary>A command that is not one line is refused with the command code.</summary>
    [Fact]
    public void A_command_that_is_not_one_line_is_refused_with_the_command_code()
    {
        var failures = Validate(Command() with { Command = "echo one\necho two" });

        Assert.Contains("CronCommandInvalid", failures);
    }

    /// <summary>An empty account is refused.</summary>
    [Fact]
    public void An_empty_account_is_refused()
    {
        Assert.NotEmpty(Validate(Command() with { AccountId = Guid.Empty }));
    }

    /// <summary>A missing schedule is refused rather than reaching the handler as null.</summary>
    [Fact]
    public void A_missing_schedule_is_refused_rather_than_reaching_the_handler_as_null()
    {
        // The handler dereferences the schedule to shape the agent call, so a null arriving there is
        // a 500 rather than a refusal a customer can act on.
        var failures = Validate(Command() with { Schedule = null! });

        Assert.Contains("CronScheduleInvalid", failures);
    }

    /// <summary>Builds a valid creation to vary one field of.</summary>
    private static CreateCronEntryCommand Command()
    {
        return new CreateCronEntryCommand(
            AccountId,
            new CronScheduleDto("0", "3", "*", "*", "*"),
            "/usr/bin/backup",
            "203.0.113.7",
            "tests");
    }

    /// <summary>Runs the validator and returns the messages it produced.</summary>
    /// <param name="command">The creation to validate.</param>
    /// <returns>The failure messages, which are this module's error codes.</returns>
    private static List<string> Validate(CreateCronEntryCommand command)
    {
        return new CreateCronEntryCommandValidator()
            .Validate(command)
            .Errors
            .Select(failure =>
            {
                return failure.ErrorMessage;
            })
            .ToList();
    }
}
