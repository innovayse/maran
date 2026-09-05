using FluentValidation;
using Maran.SharedKernel.Utilities.Mail;

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
    /// <remarks>
    /// A storage bound, not a definition of validity — the column is <c>varchar(254)</c>, so a
    /// longer address could not be written even though
    /// <see cref="EmailAddressRule.MaximumLength"/> allows the standard's full 320. The shared rule
    /// has no business knowing this table's width, so the cap stays here and stays stricter.
    /// </remarks>
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

        // EmailAddressRule, not FluentValidation's .EmailAddress(): the built-in asks only for an
        // "@" with something either side, so it accepted "Ops Team <ops@example.com>" — a display
        // name plus an address, in a field that validates neither — and every control character
        // besides. One definition of a valid address now serves the whole panel
        // (Maran.SharedKernel/Utilities/Mail).
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.EmailInvalidFormat))
            .MaximumLength(MaxEmailLength).WithMessage(nameof(Resources.ErrorMessages.EmailInvalidFormat))
            .Must(EmailAddressRule.IsAddress).WithMessage(nameof(Resources.ErrorMessages.EmailInvalidFormat));

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
