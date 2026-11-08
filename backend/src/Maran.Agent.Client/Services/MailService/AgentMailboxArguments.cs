using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Everything one creating or updating of a mailbox carries.</summary>
/// <remarks>
/// The hash is held in a non-printing wrapper: it is a credential at rest, and the record's generated
/// <c>ToString()</c> would otherwise print it.
/// </remarks>
/// <param name="AccountUsername">System username of the account the mailbox belongs to; its uid owns the mail data.</param>
/// <param name="Domain">The mail domain, already configured for the account.</param>
/// <param name="LocalPart">The part before the <c>@</c>.</param>
/// <param name="PasswordHash">
/// The login password as a hash in the scheme <see cref="AgentMailStatus.PasswordScheme"/> names, or
/// <c>null</c> to keep the current hash (refused by the agent when the mailbox is being created).
/// </param>
/// <param name="QuotaMib">The mailbox's storage ceiling in MiB; zero is refused by the agent.</param>
/// <param name="Disabled">True switches the mailbox off (no login, no new mail accepted, mail kept).</param>
/// <param name="SendLimitPerHour">The most messages the mailbox may send per hour; zero is refused by the agent.</param>
/// <param name="SpamAction">What happens to mail scored as spam.</param>
public sealed record AgentMailboxArguments(
    string AccountUsername,
    string Domain,
    string LocalPart,
    SensitiveString? PasswordHash,
    uint QuotaMib,
    bool Disabled,
    uint SendLimitPerHour,
    AgentMailSpamAction SpamAction);
