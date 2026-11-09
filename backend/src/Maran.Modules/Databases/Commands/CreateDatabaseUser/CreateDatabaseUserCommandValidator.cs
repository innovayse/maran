using FluentValidation;
using Maran.Modules.Databases.Resources;

namespace Maran.Modules.Databases.Commands.CreateDatabaseUser;

/// <summary>
/// Validates <see cref="CreateDatabaseUserCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The agent re-validates the same alphabet: this never substitutes for its boundary check.
/// </summary>
/// <remarks>
/// The alphabet and its reasons are those of the database creation validator: lowercase ASCII letters
/// and digits, anchored with <c>\z</c>, and no underscore because the account prefix's separator inside
/// a suffix would let one account ask for a name that reads as another's. Each message is a bare resx key.
/// </remarks>
public sealed class CreateDatabaseUserCommandValidator : AbstractValidator<CreateDatabaseUserCommand>
{
    /// <summary>The suffix alphabet: lowercase ASCII letters and digits, and nothing else.</summary>
    private const string SuffixPattern = @"\A[a-z0-9]+\z";

    /// <summary>A coarse ceiling; the exact fit with the account prefix is checked in the handler.</summary>
    private const int MaximumSuffixLength = 30;

    /// <summary>Configures the field rules for <see cref="CreateDatabaseUserCommand"/>.</summary>
    public CreateDatabaseUserCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.NameSuffix)
            .NotEmpty()
            .MaximumLength(MaximumSuffixLength)
            .Matches(SuffixPattern)
            .WithMessage(nameof(ErrorMessages.DatabaseUserNameInvalidFormat));
    }
}
