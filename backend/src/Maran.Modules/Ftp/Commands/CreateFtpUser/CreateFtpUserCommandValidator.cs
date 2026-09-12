using FluentValidation;
using Maran.Modules.Ftp.Resources;

namespace Maran.Modules.Ftp.Commands.CreateFtpUser;

/// <summary>
/// Validates <see cref="CreateFtpUserCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The alphabet is the whole of it, and it is deliberately narrower than "a legal Unix user name".
/// The value becomes a <c>useradd</c> argument and a directory path segment under a root-owned tree,
/// and it is compared against the daemon's own user list. Control characters and newlines are
/// refused rather than escaped (rules/security.md item 4): a value is validated, never escaped, and
/// a name that survives this rule cannot carry a line break into anything line-oriented downstream.
///
/// The separator is excluded too, and that exclusion is not tidiness: account names may contain an
/// underscore, so a suffix that could hold one would let account <c>alice</c> ask for
/// <c>bob_files</c> and be handed <c>alice_bob_files</c> — a name that reads as account <c>bob</c>'s
/// in <c>/etc/passwd</c>, in every log line and in every audit entry an operator will ever look at.
///
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead.
/// </remarks>
public sealed class CreateFtpUserCommandValidator : AbstractValidator<CreateFtpUserCommand>
{
    /// <summary>The suffix alphabet: lowercase ASCII letters and digits, and nothing else.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>. In .NET <c>$</c> also matches immediately
    /// before a trailing newline, so <c>files\n</c> satisfies a <c>$</c>-anchored pattern — and a
    /// newline in a value bound for the host's account tools is precisely what this rule exists to
    /// refuse.
    /// </remarks>
    private const string SuffixPattern = @"\A[a-z0-9]+\z";

    /// <summary>
    /// The longest suffix accepted here, before the account prefix is applied.
    /// </summary>
    /// <remarks>
    /// A coarse ceiling only. Whether the PREFIXED name fits the host's <c>useradd</c> limit depends
    /// on the account's own user name, which this validator has no way to read, so the exact check
    /// lives in the handler where the account has been resolved — and answers
    /// <c>FtpUserNameTooLong</c> there rather than an opaque refusal from the agent.
    /// </remarks>
    private const int MaximumSuffixLength = 30;

    /// <summary>Configures the field rules for <see cref="CreateFtpUserCommand"/>.</summary>
    public CreateFtpUserCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        // Every refusal a customer can reach names its own resource key. An unnamed rule is not a
        // silent pass: ExceptionMiddleware.ResolveValidationCode takes the first failure whose
        // message is itself a code, and collapses to the generic "check your input" when there is
        // none — so a thirty-one character name, which breaks no other rule, used to be answered
        // with a sentence that told the customer to check a field whose only fault was its length.
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(MaximumSuffixLength)
            .WithMessage(nameof(ErrorMessages.FtpUserNameTooLong))
            .Matches(SuffixPattern)
            .WithMessage(nameof(ErrorMessages.FtpUserNameInvalidFormat));
    }
}
