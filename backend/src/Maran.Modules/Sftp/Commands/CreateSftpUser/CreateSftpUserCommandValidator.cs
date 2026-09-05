using FluentValidation;
using Maran.Modules.Sftp.Resources;

namespace Maran.Modules.Sftp.Commands.CreateSftpUser;

/// <summary>
/// Validates <see cref="CreateSftpUserCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The alphabet is the whole of it, and it is deliberately narrower than "a legal Unix user name".
/// The value becomes a <c>useradd</c> argument, a directory path segment under a root-owned tree,
/// and a line in an <c>sshd_config</c> drop-in. <c>sshd_config</c> is line-oriented, so a newline in
/// this value would append directives of the caller's choosing to the SSH daemon's configuration.
/// Values are validated, not escaped.
///
/// The separator is excluded too, and that exclusion is not tidiness: account names may contain an
/// underscore, so a suffix that could hold one would let account <c>alice</c> ask for
/// <c>bob_deploy</c> and be handed <c>alice_bob_deploy</c> — a name that reads as account
/// <c>bob</c>'s in <c>/etc/passwd</c>, in every log line and in every audit entry an operator will
/// ever look at.
///
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead.
/// </remarks>
public sealed class CreateSftpUserCommandValidator : AbstractValidator<CreateSftpUserCommand>
{
    /// <summary>The suffix alphabet: lowercase ASCII letters and digits, and nothing else.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>. In .NET <c>$</c> also matches immediately
    /// before a trailing newline, so <c>deploy\n</c> satisfies a <c>$</c>-anchored pattern — and a
    /// newline in a value bound for an <c>sshd_config</c> drop-in is precisely what this rule exists
    /// to refuse.
    /// </remarks>
    private const string SuffixPattern = @"\A[a-z0-9]+\z";

    /// <summary>
    /// The longest suffix accepted here, before the account prefix is applied.
    /// </summary>
    /// <remarks>
    /// A coarse ceiling only. Whether the PREFIXED name fits the host's <c>useradd</c> limit depends
    /// on the account's own user name, which this validator has no way to read, so the exact check
    /// lives in the handler where the account has been resolved — and answers
    /// <c>SftpUserNameTooLong</c> there rather than an opaque refusal from the agent.
    /// </remarks>
    private const int MaximumSuffixLength = 30;

    /// <summary>Configures the field rules for <see cref="CreateSftpUserCommand"/>.</summary>
    public CreateSftpUserCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(MaximumSuffixLength)
            .Matches(SuffixPattern)
            .WithMessage(nameof(ErrorMessages.SftpUserNameInvalidFormat));
    }
}
