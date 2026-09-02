using FluentValidation;
using Maran.Modules.Databases.Resources;

namespace Maran.Modules.Databases.Commands.CreateDatabase;

/// <summary>
/// Validates <see cref="CreateDatabaseCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The alphabet is the whole of it, and it is deliberately narrower than "a legal MySQL identifier".
/// Both names are interpolated into DDL in a root MySQL session — <c>CREATE DATABASE `name`</c> and
/// <c>CREATE USER '&lt;name&gt;'@'localhost'</c> — which takes no placeholders, so what makes the
/// interpolation safe is that neither value can hold a backtick, a quote, a backslash, a semicolon,
/// a space or a newline. Values are validated, not escaped.
///
/// The separator is excluded too, and that exclusion is not tidiness: account names may contain an
/// underscore, so a suffix that could hold one would let account <c>alice</c> ask for
/// <c>bob_secrets</c> and be handed <c>alice_bob_secrets</c> — a name that reads as account
/// <c>bob</c>'s in every listing, log line and backup file an operator will ever look at.
///
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead.
/// </remarks>
public sealed class CreateDatabaseCommandValidator : AbstractValidator<CreateDatabaseCommand>
{
    /// <summary>The suffix alphabet: lowercase ASCII letters and digits, and nothing else.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>. In .NET <c>$</c> also matches immediately
    /// before a trailing newline, so <c>shop\n</c> satisfies a <c>$</c>-anchored pattern — and a
    /// newline in a value bound for a SQL statement is precisely what this rule exists to refuse.
    /// </remarks>
    private const string SuffixPattern = @"\A[a-z0-9]+\z";

    /// <summary>
    /// The longest suffix accepted here, before the account prefix is applied.
    /// </summary>
    /// <remarks>
    /// A coarse ceiling only. Whether the PREFIXED name fits MySQL's limits depends on the account's
    /// own user name, which this validator has no way to read, so the exact check lives in the
    /// handler where the account has been resolved — and answers <c>DatabaseNameTooLong</c> there
    /// rather than an opaque refusal from the agent.
    /// </remarks>
    private const int MaximumSuffixLength = 30;

    /// <summary>Configures the field rules for <see cref="CreateDatabaseCommand"/>.</summary>
    public CreateDatabaseCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(MaximumSuffixLength)
            .Matches(SuffixPattern)
            .WithMessage(nameof(ErrorMessages.DatabaseNameInvalidFormat));

        RuleFor(command => command.DbUserName)
            .NotEmpty()
            .MaximumLength(MaximumSuffixLength)
            .Matches(SuffixPattern)
            .WithMessage(nameof(ErrorMessages.DatabaseUserNameInvalidFormat));
    }
}
