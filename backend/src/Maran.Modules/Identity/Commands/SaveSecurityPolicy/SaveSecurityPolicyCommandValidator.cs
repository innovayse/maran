using FluentValidation;

namespace Maran.Modules.Identity.Commands.SaveSecurityPolicy;

/// <summary>
/// Bounds the panel's security policy. Every rule here exists because the value outside it makes a
/// protection stop protecting, silently.
/// </summary>
public sealed class SaveSecurityPolicyCommandValidator : AbstractValidator<SaveSecurityPolicyCommand>
{
    /// <summary>Shortest minimum this panel allows an operator to choose.</summary>
    /// <remarks>
    /// Eight, and not lower. An operator may relax the default of twelve — they run the server — but
    /// a policy that permitted a four-character minimum would let one careless setting turn every
    /// account on the machine into a PIN, and a panel that offers that option will eventually be
    /// found with it switched on.
    /// </remarks>
    private const int MinimumAllowedPasswordLength = 8;

    /// <summary>Longest minimum this panel allows, kept under the login endpoint's own password ceiling.</summary>
    private const int MaximumAllowedPasswordLength = 128;

    /// <summary>Fewest failures that may lock an account.</summary>
    /// <remarks>
    /// Three. Below that, an account locks on the number of mistakes an ordinary person makes with a
    /// keyboard layout they are not used to — and since anyone can trigger it by guessing at a
    /// username, a threshold of one is a denial-of-service switch for every account on the panel.
    /// </remarks>
    private const int MinimumAllowedFailedAttempts = 3;

    /// <summary>Most failures that may be allowed before a lock.</summary>
    private const int MaximumAllowedFailedAttempts = 100;

    /// <summary>Shortest lockout, in minutes.</summary>
    private const int MinimumLockoutMinutes = 1;

    /// <summary>Longest lockout, in minutes: one day.</summary>
    /// <remarks>
    /// A ceiling rather than an unbounded number, because the lockout is triggered by ANYBODY who can
    /// name a username. A policy of a year would hand an attacker a permanent denial of service
    /// against a named administrator for the cost of ten wrong passwords.
    /// </remarks>
    private const int MaximumLockoutMinutes = 1_440;

    /// <summary>Configures the field rules for <see cref="SaveSecurityPolicyCommand"/>.</summary>
    public SaveSecurityPolicyCommandValidator()
    {
        RuleFor(command => command.MinimumPasswordLength)
            .InclusiveBetween(MinimumAllowedPasswordLength, MaximumAllowedPasswordLength)
            .WithMessage(nameof(Resources.ErrorMessages.SecurityPolicyInvalid));

        RuleFor(command => command.MaxFailedLoginAttempts)
            .InclusiveBetween(MinimumAllowedFailedAttempts, MaximumAllowedFailedAttempts)
            .WithMessage(nameof(Resources.ErrorMessages.SecurityPolicyInvalid));

        RuleFor(command => command.LockoutMinutes)
            .InclusiveBetween(MinimumLockoutMinutes, MaximumLockoutMinutes)
            .WithMessage(nameof(Resources.ErrorMessages.SecurityPolicyInvalid));
    }
}
