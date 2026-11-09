using FluentValidation;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Utilities.Mail;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Validates <see cref="CreateAccountCommand"/> before it reaches the handler (rules/security.md
/// "Input"). <see cref="CreateAccountCommand.Name"/> is re-validated inside the agent too, which
/// refuses a reserved name and any user that is not a hosting account, because the API's validation
/// never substitutes for the agent's own boundary check.
/// </summary>
public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    /// <summary>
    /// The agent's account-name alphabet, exactly: a lowercase letter, then 2 to 29 lowercase
    /// letters, digits or underscores (3 to 30 characters). It mirrors <c>AccountName::parse</c> in
    /// <c>agent/crates/agent-core/src/validation/system/name.rs</c>, so the API and the agent stop
    /// disagreeing about which names are valid, and a validated name is always safe to become the
    /// account's Linux user name, a home directory and a systemd unit name. A hyphen is not in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anchored with <c>\A…\z</c>, never <c>^…$</c>. In .NET <c>$</c> also matches immediately
    /// before a trailing newline, so a <c>$</c>-anchored pattern accepts <c>acme\n</c> — and this
    /// exact value is handed to the agent as a system user name by
    /// <c>CreateAccountCommandHandler</c>. rules/security.md item 4 requires the boundary to refuse
    /// it, and the agent's own <c>AccountName::parse</c> refuses it too; that is defence in depth,
    /// not a reason for the API to let it past.
    /// </para>
    /// <para>
    /// It stays here rather than moving to <c>SharedKernel/Utilities/</c> because it is asked in
    /// exactly one place. A helper earns that home by answering a general question the panel asks in
    /// more than one place (rules/csharp.md "Cross-cutting infrastructure"); a rule with one call
    /// site moved down there would be a home built for a duplicate that does not exist. The
    /// reserved-name rule that follows the pattern is <see cref="ReservedAccountNamePolicy"/>, in
    /// this module for the same reason.
    /// </para>
    /// </remarks>
    private const string LinuxUserNamePattern = @"\A[a-z][a-z0-9_]{2,29}\z";

    /// <summary>The longest account name the agent accepts.</summary>
    private const int NameMaximumLength = 30;

    /// <summary>
    /// The machine code for a name outside the alphabet or length. It is passed as the validation
    /// message itself, not as an error code: <c>ExceptionMiddleware</c> forwards only a message that
    /// is entirely alphanumeric and resolves it as a resx key, and discards an English sentence.
    /// </summary>
    private const string NameInvalidMessage = nameof(ErrorMessages.AccountNameInvalid);

    /// <summary>The machine code for a reserved name, delivered the same way as <see cref="NameInvalidMessage"/>.</summary>
    private const string NameReservedMessage = nameof(ErrorMessages.AccountNameReserved);

    /// <summary>
    /// The machine code for a primary domain written with a leading <c>www.</c>, delivered the same
    /// way as <see cref="NameInvalidMessage"/>. The account's primary domain is the bare name; a
    /// leading <c>www.</c> would make every later decision about the bare and the <c>www</c> host
    /// start from the wrong one (issue #194).
    /// </summary>
    private const string DomainWwwPrefixMessage = nameof(ErrorMessages.AccountDomainWwwPrefix);

    /// <summary>The leading label <see cref="DomainWwwPrefixMessage"/> refuses, compared without regard to case.</summary>
    private const string WwwPrefix = "www.";

    /// <summary>The machine code for an owner language the panel ships no text in.</summary>
    private const string OwnerLanguageUnsupportedMessage = nameof(ErrorMessages.AccountOwnerLanguageUnsupported);

    /// <summary>The machine code for a login mode and an initial password that do not belong together.</summary>
    private const string InitialPasswordInvalidMessage = nameof(ErrorMessages.AccountInitialPasswordInvalid);

    /// <summary>The machine code for a process limit outside <c>1</c> to <see cref="MaxProcessLimit"/>.</summary>
    private const string ProcessLimitInvalidMessage = nameof(ErrorMessages.AccountProcessLimitInvalid);

    /// <summary>The machine code for an inode limit outside <c>0</c> to <see cref="MaxInodeLimit"/>.</summary>
    private const string InodeLimitInvalidMessage = nameof(ErrorMessages.AccountInodeLimitInvalid);

    /// <summary>The machine code for an open-files limit outside <c>0</c> to <see cref="MaxOpenFilesLimit"/>.</summary>
    private const string OpenFilesLimitInvalidMessage = nameof(ErrorMessages.AccountOpenFilesLimitInvalid);

    /// <summary>
    /// The largest process limit the agent accepts: the kernel's <c>PID_MAX_LIMIT</c> on 64-bit hosts.
    /// The agent's own bound (<c>tasks_max::MAXIMUM</c>); the two must stay equal.
    /// </summary>
    private const int MaxProcessLimit = 4_194_304;

    /// <summary>
    /// The largest inode limit the agent accepts: the quota tool takes 32-bit figures, so
    /// <c>u32::MAX</c> (<c>InodeLimit</c> in <c>agent-core</c>). The command carries a 64-bit integer
    /// precisely so this whole range is reachable; the two must stay equal.
    /// </summary>
    private const long MaxInodeLimit = uint.MaxValue;

    /// <summary>
    /// The largest open-files limit the agent accepts (<c>open_files_limit::MAXIMUM</c>, the kernel's
    /// default <c>nr_open</c>); the two must stay equal.
    /// </summary>
    private const int MaxOpenFilesLimit = 1_048_576;

    /// <summary>
    /// A storage bound, not a definition of validity — <c>AccountConfiguration</c> maps
    /// <c>Account.OwnerEmail</c> as <c>varchar(256)</c>, narrower than
    /// <see cref="EmailAddressRule.MaximumLength"/>'s full 320. The shared rule has no business
    /// knowing this table's width, so the cap stays here and stays stricter — the same shape
    /// <c>CompleteSetupCommandValidator.MaxEmailLength</c> uses for its own, differently-sized column.
    /// </summary>
    private const int MaxOwnerEmailLength = 256;

    /// <summary>Configures the field rules for <see cref="CreateAccountCommand"/>.</summary>
    public CreateAccountCommandValidator()
    {
        // Stop at the first failure so a malformed name is told it is malformed, and only a
        // well-formed one is asked whether it is reserved.
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(NameInvalidMessage)
            .MaximumLength(NameMaximumLength)
            .WithMessage(NameInvalidMessage)
            .Matches(LinuxUserNamePattern)
            .WithMessage(NameInvalidMessage)
            .Must(name =>
            {
                return !ReservedAccountNamePolicy.IsReserved(name);
            })
            .WithMessage(NameReservedMessage);

        RuleFor(command => command.PrimaryDomain)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage("PrimaryDomain must be a valid domain name.")
            .Must(domain =>
            {
                return !domain.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase);
            })
            .WithMessage(DomainWwwPrefixMessage);

        RuleFor(command => command.OwnerLanguage)
            .Must(PanelLanguages.IsSupported)
            .WithMessage(OwnerLanguageUnsupportedMessage);

        // Whether the plan exists is NOT asked here, on purpose. A plan a caller may not see is the
        // same as a plan that does not exist (a reseller is narrowed to its own plans), and the
        // handler is the one place that answers it: 404 PlanNotFound, journalled as a refusal. A
        // validator rule here answered the caller 400 with the generic code instead, before the
        // handler ran, so the refusal was neither the contract's status nor in the audit trail.
        RuleFor(command => command.PlanId)
            .NotEmpty();

        // EmailAddressRule, not FluentValidation's .EmailAddress(), for the reason
        // CompleteSetupCommandValidator's own comment states: the built-in asks only for an "@"
        // with something either side, so it accepts a display-name form like
        // "Ops Team <ops@example.com>" and every control character besides. This value is
        // reached at — it becomes User.Email and is handed to SendMailRequested.Recipient — so
        // rules/security.md item 4 (a caller-supplied value ending up in line-oriented output)
        // applies to it exactly as it does to every other address in this tree.
        RuleFor(command => command.OwnerEmail)
            .NotEmpty()
            .MaximumLength(MaxOwnerEmailLength)
            .Must(EmailAddressRule.IsAddress);

        // The mode and the password travel together or not at all. Only the coupling and non-blank are
        // asked here: whether the password is strong enough is the security policy's, which Identity
        // owns, so there is one definition of "strong enough". The handler asks it through
        // IInitialPasswordPolicy before it provisions anything.
        RuleFor(command => command)
            .Must(HasPasswordExactlyWhenModeNeedsOne)
            .OverridePropertyName(nameof(CreateAccountCommand.InitialPassword))
            .WithMessage(InitialPasswordInvalidMessage);

        // Zero is refused for processes: it is not a smaller allowance but an account that cannot run.
        // For inodes and open files it is the way to say "no limit", the agent's own reading. A limit
        // left out binds to null, which these rules let through: it means "follow the plan".
        RuleFor(command => command.ProcessLimit)
            .InclusiveBetween(1, MaxProcessLimit)
            .WithMessage(ProcessLimitInvalidMessage);

        RuleFor(command => command.InodeLimit)
            .InclusiveBetween(0L, MaxInodeLimit)
            .WithMessage(InodeLimitInvalidMessage);

        RuleFor(command => command.OpenFilesLimit)
            .InclusiveBetween(0, MaxOpenFilesLimit)
            .WithMessage(OpenFilesLimitInvalidMessage);
    }

    /// <summary>Whether the command states a password exactly when its login mode is the one that needs it.</summary>
    /// <param name="command">The command being validated.</param>
    /// <returns>
    /// <c>true</c> for an invitation with no password, or for an initial password mode with a
    /// non-blank one; <c>false</c> for any other pairing, including a mode that is no member of the enum.
    /// </returns>
    private static bool HasPasswordExactlyWhenModeNeedsOne(CreateAccountCommand command)
    {
        return command.LoginMode switch
        {
            OwnerLoginMode.Invitation => command.InitialPassword is null,
            OwnerLoginMode.InitialPassword => !string.IsNullOrWhiteSpace(command.InitialPassword?.Reveal()),
            _ => false,
        };
    }
}
