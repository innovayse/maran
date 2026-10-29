using FluentValidation.TestHelper;
using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Domain.Entities;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>
/// Behavioral contract of <see cref="CreatePlanCommandValidator"/> (and, through it, the shared
/// <c>PlanDefinitionValidator</c> every plan edit is also held to). The limits become kernel
/// quotas, php-fpm <c>pm.max_children</c> values and creation gates, so each boundary is pinned on
/// both sides: the value that must be refused and the value next to it that must be accepted.
/// </summary>
public sealed class CreatePlanCommandValidatorTests
{
    /// <summary>The validator under test; it has no dependency, so one instance serves every test.</summary>
    private readonly CreatePlanCommandValidator _validator = new();

    /// <summary>Builds a command that satisfies every rule, so a single field can be broken per test.</summary>
    private static CreatePlanCommand ValidCommand()
    {
        return new("Agency", 10_240, 10, 5, 5, 0, 0, 8, IpAddress: "203.0.113.7", UserAgent: "unit-tests");
    }

    /// <summary>A fully valid command passes every rule.</summary>
    [Fact]
    public async Task A_fully_valid_command_passes_every_rule()
    {
        var result = await _validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A name that is empty or only white space fails on the name.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_that_is_empty_or_only_white_space_fails_on_the_name(string name)
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { Name = name });

        result.ShouldHaveValidationErrorFor(command => command.Name);
    }

    /// <summary>A name of exactly the maximum length is accepted and one character more is refused.</summary>
    [Fact]
    public async Task A_name_of_exactly_the_maximum_length_is_accepted_and_one_character_more_is_refused()
    {
        var atLimit = await _validator.TestValidateAsync(ValidCommand() with { Name = new string('a', Plan.NameMaxLength) });
        var overLimit = await _validator.TestValidateAsync(
            ValidCommand() with { Name = new string('a', Plan.NameMaxLength + 1) });

        atLimit.ShouldNotHaveValidationErrorFor(command => command.Name);
        overLimit.ShouldHaveValidationErrorFor(command => command.Name);
    }

    /// <summary>A name carrying a control character fails on the name.</summary>
    [Theory]
    [InlineData("Agency\nPlan")]
    [InlineData("Agency\tPlan")]
    [InlineData("Agency\u0000Plan")]
    public async Task A_name_carrying_a_control_character_fails_on_the_name(string name)
    {
        // The name is shown to customers and written into the audit journal's subject, which an
        // operator reads as one line: a line break in it would let one plan forge a second journal row.
        var result = await _validator.TestValidateAsync(ValidCommand() with { Name = name });

        result.ShouldHaveValidationErrorFor(command => command.Name);
    }

    /// <summary>A disk quota below one megabyte is refused and one megabyte is accepted.</summary>
    [Fact]
    public async Task A_disk_quota_below_one_megabyte_is_refused_and_one_megabyte_is_accepted()
    {
        var zero = await _validator.TestValidateAsync(ValidCommand() with { DiskQuotaMb = 0 });
        var one = await _validator.TestValidateAsync(ValidCommand() with { DiskQuotaMb = 1 });

        zero.ShouldHaveValidationErrorFor(command => command.DiskQuotaMb);
        one.ShouldNotHaveValidationErrorFor(command => command.DiskQuotaMb);
    }

    /// <summary>A disk quota above one pebibyte is refused and one pebibyte exactly is accepted.</summary>
    [Fact]
    public async Task A_disk_quota_above_one_pebibyte_is_refused_and_one_pebibyte_exactly_is_accepted()
    {
        // The quota travels to the agent in bytes (megabytes * 1024 * 1024); the ceiling is a typo
        // and overflow guard far above anything a server can honour, not a policy.
        const int OnePebibyteInMegabytes = 1_073_741_824;
        var over = await _validator.TestValidateAsync(ValidCommand() with { DiskQuotaMb = int.MaxValue });
        var at = await _validator.TestValidateAsync(ValidCommand() with { DiskQuotaMb = OnePebibyteInMegabytes });

        over.ShouldHaveValidationErrorFor(command => command.DiskQuotaMb);
        at.ShouldNotHaveValidationErrorFor(command => command.DiskQuotaMb);
    }

    /// <summary>Each allowance that must be positive refuses zero and a negative number.</summary>
    [Theory]
    [InlineData("MaxSites")]
    [InlineData("MaxDatabases")]
    [InlineData("MaxSftpUsers")]
    [InlineData("MaxPhpWorkersPerPool")]
    public async Task Each_allowance_that_must_be_positive_refuses_zero_and_a_negative_number(string property)
    {
        // Zero here is a plan that cannot be used at all (no site, no database, no way to upload
        // files, a php-fpm pool that will not start), and the refusal when it bites names the plan
        // rather than the mistake — so it is stopped at the form instead.
        foreach (var value in new[] { 0, -1 })
        {
            var result = await _validator.TestValidateAsync(WithLimit(property, value));

            result.ShouldHaveValidationErrorFor(property);
        }
    }

    /// <summary>Each allowance that may be zero accepts zero and refuses a negative number.</summary>
    [Theory]
    [InlineData("MaxFtpUsers")]
    [InlineData("MaxCronEntries")]
    public async Task Each_allowance_that_may_be_zero_accepts_zero_and_refuses_a_negative_number(string property)
    {
        // The inverse control for the theory above: "this tier has no FTPS login / no scheduled
        // tasks" is a product an operator may sell, so zero must pass where the others refuse it.
        var zero = await _validator.TestValidateAsync(WithLimit(property, 0));
        var negative = await _validator.TestValidateAsync(WithLimit(property, -1));

        zero.ShouldNotHaveValidationErrorFor(property);
        negative.ShouldHaveValidationErrorFor(property);
    }

    /// <summary>An absurdly large allowance is refused rather than stored.</summary>
    [Theory]
    [InlineData("MaxSites")]
    [InlineData("MaxDatabases")]
    [InlineData("MaxSftpUsers")]
    [InlineData("MaxFtpUsers")]
    [InlineData("MaxCronEntries")]
    [InlineData("MaxPhpWorkersPerPool")]
    public async Task An_absurdly_large_allowance_is_refused_rather_than_stored(string property)
    {
        var result = await _validator.TestValidateAsync(WithLimit(property, int.MaxValue));

        result.ShouldHaveValidationErrorFor(property);
    }

    /// <summary>Builds a valid command with one named limit set to <paramref name="value"/>.</summary>
    /// <param name="property">The command property to break.</param>
    /// <param name="value">The value to give it.</param>
    private static CreatePlanCommand WithLimit(string property, int value)
    {
        var command = ValidCommand();
        return property switch
        {
            "MaxSites" => command with { MaxSites = value },
            "MaxDatabases" => command with { MaxDatabases = value },
            "MaxSftpUsers" => command with { MaxSftpUsers = value },
            "MaxFtpUsers" => command with { MaxFtpUsers = value },
            "MaxCronEntries" => command with { MaxCronEntries = value },
            "MaxPhpWorkersPerPool" => command with { MaxPhpWorkersPerPool = value },
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Not a plan limit."),
        };
    }
}
