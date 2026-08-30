using FluentValidation;

namespace Maran.Modules.Identity.Commands.CompleteSetup;

/// <summary>The panel's password policy, and the shape of the first administrator's details.</summary>
public sealed class CompleteSetupCommandValidator : AbstractValidator<CompleteSetupCommand>
{
    /// <summary>Shortest password the panel accepts.</summary>
    private const int MinPasswordLength = 12;

    /// <summary>Longest password accepted, bounding the work Argon2id is asked to do.</summary>
    private const int MaxPasswordLength = 256;

    /// <summary>Longest username the panel stores, matching the column.</summary>
    private const int MaxUsernameLength = 64;

    /// <summary>Longest address the panel stores, matching the column.</summary>
    private const int MaxEmailLength = 254;

    /// <summary>Characters a username may contain: what a Linux login name and a URL both tolerate.</summary>
    private const string UsernamePattern = "^[a-zA-Z0-9._-]+$";

    /// <summary>Configures the field rules for <see cref="CompleteSetupCommand"/>.</summary>
    public CompleteSetupCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty();

        // WithMessage binds to the rule immediately before it, so each rule that needs a code
        // carries its own. Chaining several checks and appending one WithMessage at the end leaves
        // the earlier ones with FluentValidation's default English sentence — which reaches the
        // customer as a generic "some details are not valid" instead of the rule they broke.
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.UsernameInvalidFormat))
            .MaximumLength(MaxUsernameLength).WithMessage(nameof(Resources.ErrorMessages.UsernameInvalidFormat))
            .Matches(UsernamePattern).WithMessage(nameof(Resources.ErrorMessages.UsernameInvalidFormat));

        RuleFor(command => command.Email)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.EmailInvalidFormat))
            .EmailAddress().WithMessage(nameof(Resources.ErrorMessages.EmailInvalidFormat))
            .MaximumLength(MaxEmailLength).WithMessage(nameof(Resources.ErrorMessages.EmailInvalidFormat));

        // Length, and not being the username, are the whole policy: the two rules that actually
        // stop the passwords people pick under time pressure while installing a server. Character-
        // class requirements mostly produce "Password1!" and a note stuck to a monitor.
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak))
            .MinimumLength(MinPasswordLength).WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak))
            .MaximumLength(MaxPasswordLength).WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak))
            .NotEqual(command => command.Username).WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak));

    }
}
