using FluentValidation.TestHelper;
using Maran.Modules.Accounts.Commands.UpdatePlan;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// Behavioral contract of <see cref="UpdatePlanCommandValidator"/>. It shares every rule with the
/// create validator through one base type, so the exhaustive boundaries live in
/// <c>CreatePlanCommandValidatorTests</c>; what is pinned here is that an EDIT is held to the same
/// rules at all — an update path that skipped them would be the way to write a plan the create path
/// refuses.
/// </summary>
public sealed class UpdatePlanCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly UpdatePlanCommandValidator _validator = new();

    /// <summary>Builds a command that satisfies every rule.</summary>
    private static UpdatePlanCommand ValidCommand()
    {
        return new("Agency", 10_240, 10, 5, 5, 0, 0, 8, Guid.NewGuid(), "203.0.113.7", "unit-tests");
    }

    /// <summary>A fully valid command passes every rule.</summary>
    [Fact]
    public async Task A_fully_valid_command_passes_every_rule()
    {
        var result = await _validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An edit that blanks the name is refused like a creation would be.</summary>
    [Fact]
    public async Task An_edit_that_blanks_the_name_is_refused_like_a_creation_would_be()
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { Name = " " });

        result.ShouldHaveValidationErrorFor(command => command.Name);
    }

    /// <summary>An edit that zeroes a limit a plan cannot work without is refused.</summary>
    [Fact]
    public async Task An_edit_that_zeroes_a_limit_a_plan_cannot_work_without_is_refused()
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { MaxPhpWorkersPerPool = 0, DiskQuotaMb = 0 });

        result.ShouldHaveValidationErrorFor(command => command.MaxPhpWorkersPerPool);
        result.ShouldHaveValidationErrorFor(command => command.DiskQuotaMb);
    }

    /// <summary>An edit that zeroes an allowance a tier may do without is accepted.</summary>
    [Fact]
    public async Task An_edit_that_zeroes_an_allowance_a_tier_may_do_without_is_accepted()
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { MaxFtpUsers = 0, MaxCronEntries = 0 });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
