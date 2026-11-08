using FluentValidation.TestHelper;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// The rules <see cref="CreateAccountCommandValidator"/> holds for the options added in issue #194:
/// the coupling of the login mode and the initial password, and the ranges of the three ceilings,
/// which are the agent's own (a process limit of at least one; zero for inodes and open files is
/// "no limit"). Strength of the password is Identity's rule and is not asked here.
/// </summary>
public sealed class CreateAccountCommandValidatorOptionsTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateAccountCommandValidator _validator = new();

    /// <summary>Builds a command that satisfies every rule, so one option can be broken per test.</summary>
    /// <returns>The command.</returns>
    private static CreateAccountCommand ValidCommand()
    {
        return new CreateAccountCommand(
            "acme", "acme.example.com", Guid.NewGuid(), "owner@example.com", IpAddress: "203.0.113.7", UserAgent: "unit-tests");
    }

    /// <summary>A command that states none of the options passes: every caller that predates them keeps working.</summary>
    [Fact]
    public async Task A_command_that_states_none_of_the_options_passes()
    {
        var result = await _validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An initial password with its mode passes.</summary>
    [Fact]
    public async Task An_initial_password_with_its_mode_passes()
    {
        var command = ValidCommand() with
        {
            LoginMode = OwnerLoginMode.InitialPassword,
            InitialPassword = new SensitiveString("Correct-Horse-9"),
        };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>The initial password mode without a password, or with a blank one, is refused.</summary>
    /// <param name="password">The password stated, or null for none.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_initial_password_mode_without_a_usable_password_is_refused(string? password)
    {
        var command = ValidCommand() with
        {
            LoginMode = OwnerLoginMode.InitialPassword,
            InitialPassword = password is null ? null : new SensitiveString(password),
        };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(nameof(CreateAccountCommand.InitialPassword))
            .WithErrorMessage("AccountInitialPasswordInvalid");
    }

    /// <summary>An invitation that also states a password is refused, so a password is never quietly dropped.</summary>
    [Fact]
    public async Task An_invitation_that_also_states_a_password_is_refused()
    {
        var command = ValidCommand() with { InitialPassword = new SensitiveString("Correct-Horse-9") };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(nameof(CreateAccountCommand.InitialPassword))
            .WithErrorMessage("AccountInitialPasswordInvalid");
    }

    /// <summary>A login mode that is no member of the enum is refused.</summary>
    [Fact]
    public async Task A_login_mode_that_is_no_member_of_the_enum_is_refused()
    {
        var command = ValidCommand() with { LoginMode = (OwnerLoginMode)99 };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(nameof(CreateAccountCommand.InitialPassword));
    }

    /// <summary>A process limit of zero or below, or above the agent's bound, is refused.</summary>
    /// <param name="processes">The figure stated.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4_194_305)]
    public async Task A_process_limit_outside_one_to_the_agents_bound_is_refused(int processes)
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { ProcessLimit = processes });

        result.ShouldHaveValidationErrorFor(c => c.ProcessLimit).WithErrorMessage("AccountProcessLimitInvalid");
    }

    /// <summary>The ends of the process range are accepted, so the bound is the agent's and not one less.</summary>
    /// <param name="processes">The figure stated.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(4_194_304)]
    public async Task The_ends_of_the_process_range_are_accepted(int processes)
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { ProcessLimit = processes });

        result.ShouldNotHaveValidationErrorFor(c => c.ProcessLimit);
    }

    /// <summary>An inode limit below zero or above the agent's bound is refused; zero (no limit) and the bound are accepted.</summary>
    /// <param name="inodes">The figure stated.</param>
    /// <param name="accepted">Whether the validator should accept it.</param>
    [Theory]
    [InlineData(-1L, false)]
    [InlineData(4_294_967_296L, false)]
    [InlineData(0L, true)]
    [InlineData(4_294_967_295L, true)]
    public async Task The_inode_range_is_zero_to_the_agents_bound(long inodes, bool accepted)
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { InodeLimit = inodes });

        if (accepted)
        {
            result.ShouldNotHaveValidationErrorFor(c => c.InodeLimit);
        }
        else
        {
            result.ShouldHaveValidationErrorFor(c => c.InodeLimit).WithErrorMessage("AccountInodeLimitInvalid");
        }
    }

    /// <summary>An open-files limit below zero or above the agent's bound is refused; zero (no limit) and the bound are accepted.</summary>
    /// <param name="openFiles">The figure stated.</param>
    /// <param name="accepted">Whether the validator should accept it.</param>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(1_048_577, false)]
    [InlineData(0, true)]
    [InlineData(1_048_576, true)]
    public async Task The_open_files_range_is_zero_to_the_agents_bound(int openFiles, bool accepted)
    {
        var result = await _validator.TestValidateAsync(ValidCommand() with { OpenFilesLimit = openFiles });

        if (accepted)
        {
            result.ShouldNotHaveValidationErrorFor(c => c.OpenFilesLimit);
        }
        else
        {
            result.ShouldHaveValidationErrorFor(c => c.OpenFilesLimit).WithErrorMessage("AccountOpenFilesLimitInvalid");
        }
    }
}
