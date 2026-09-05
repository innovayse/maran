using Maran.Modules.Cron.Commands.SetCronEnvironment;
using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Tests.Commands.SetCronEnvironment;

/// <summary>The rules that belong to the environment SET rather than to any one assignment.</summary>
public sealed class SetCronEnvironmentCommandValidatorTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>An ordinary set passes.</summary>
    [Fact]
    public void An_ordinary_set_passes()
    {
        Assert.Empty(Validate(
        [
            new CronEnvironmentVariableDto("PATH", "/usr/bin"),
            new CronEnvironmentVariableDto("TZ", "UTC"),
        ]));
    }

    /// <summary>An empty set passes because clearing every assignment is a real request.</summary>
    [Fact]
    public void An_empty_set_passes_because_clearing_every_assignment_is_a_real_request()
    {
        // NotEmpty here would remove the only way back from a preamble a customer no longer wants.
        Assert.Empty(Validate([]));
    }

    /// <summary>The same name twice is refused rather than silently deduplicated.</summary>
    [Fact]
    public void The_same_name_twice_is_refused_rather_than_silently_deduplicated()
    {
        // Two assignments to one name are two crontab lines of which cron applies the last, so the
        // panel would show a value the host does not use. Which of the two the customer meant is not
        // something this layer can know, so it asks rather than guessing.
        var failures = Validate(
        [
            new CronEnvironmentVariableDto("TZ", "UTC"),
            new CronEnvironmentVariableDto("TZ", "Europe/Yerevan"),
        ]);

        Assert.Contains("CronEnvironmentDuplicateName", failures);
    }

    /// <summary>More assignments than the managed preamble may hold is refused.</summary>
    [Fact]
    public void More_assignments_than_the_managed_preamble_may_hold_is_refused()
    {
        var many = Enumerable.Range(0, 33)
            .Select(index =>
            {
                return new CronEnvironmentVariableDto($"VAR_{index:D2}", "x");
            })
            .ToList();

        Assert.Contains("CronEnvironmentTooManyVariables", Validate(many));
    }

    /// <summary>One bad assignment inside an otherwise good set is refused with its own code.</summary>
    [Fact]
    public void One_bad_assignment_inside_an_otherwise_good_set_is_refused_with_its_own_code()
    {
        // RuleForEach, asserted rather than assumed: a set validator that checked only the collection
        // would let every individual assignment through unchecked while looking thorough.
        var failures = Validate(
        [
            new CronEnvironmentVariableDto("PATH", "/usr/bin"),
            new CronEnvironmentVariableDto("MAILTO", "someone@example.test"),
        ]);

        Assert.Contains("CronEnvironmentNameReserved", failures);
    }

    /// <summary>Runs the validator and returns the messages it produced.</summary>
    /// <param name="variables">The set to validate.</param>
    /// <returns>The failure messages, which are this module's error codes.</returns>
    private static List<string> Validate(IReadOnlyList<CronEnvironmentVariableDto> variables)
    {
        return new SetCronEnvironmentCommandValidator()
            .Validate(new SetCronEnvironmentCommand(AccountId, variables, "203.0.113.7", "tests"))
            .Errors
            .Select(failure =>
            {
                return failure.ErrorMessage;
            })
            .ToList();
    }
}
