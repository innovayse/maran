using FluentValidation;

namespace Maran.Modules.Identity.Commands.Login;

/// <summary>Bounds the fields of <see cref="LoginCommand"/> before they reach the password hasher.</summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>Longest username the panel stores, matching the column.</summary>
    private const int MaxUsernameLength = 64;

    /// <summary>Longest password accepted for an attempt.</summary>
    private const int MaxPasswordLength = 256;

    /// <summary>Configures the field rules for <see cref="LoginCommand"/>.</summary>
    public LoginCommandValidator()
    {
        // The same code as a wrong password, deliberately: an empty or overlong username is not a
        // different answer to "who are you", and a distinct one would tell a caller which of their
        // guesses was even well-formed.
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvalidCredentialsUnauthorized))
            .MaximumLength(MaxUsernameLength).WithMessage(nameof(Resources.ErrorMessages.InvalidCredentialsUnauthorized));

        // The upper bound is a denial-of-service guard rather than a password policy: Argon2id
        // deliberately costs 64 MiB and three passes, so an unbounded password is an unbounded
        // amount of both — per attempt, on an endpoint anyone may call.
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvalidCredentialsUnauthorized))
            .MaximumLength(MaxPasswordLength).WithMessage(nameof(Resources.ErrorMessages.InvalidCredentialsUnauthorized));
    }
}
