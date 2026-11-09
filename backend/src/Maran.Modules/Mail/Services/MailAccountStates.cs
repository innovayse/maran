using Maran.Modules.Mail.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Services;

/// <summary>Reads whether accounts are suspended, which is all a domain or a mailbox needs to know about its account's state.</summary>
/// <remarks>
/// A suspension is applied to the host as a whole and is not copied into this module's rows, so there is
/// one place it is true: the account. It is read through the tenant-scoped <see cref="IAccountDirectory"/>
/// window, so a caller is told the state of its own accounts only; an account the window does not name reads
/// as active, which is the permissive direction (rules/security.md: a client-side or advisory check errs
/// permissive, enforcement is the host's).
/// </remarks>
public sealed class MailAccountStates
{
    /// <summary>The window onto the Accounts module, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the reader.</summary>
    /// <param name="accounts">The window onto the Accounts module.</param>
    public MailAccountStates(IAccountDirectory accounts)
    {
        _accounts = accounts;
    }

    /// <summary>Reads which of the given accounts are suspended.</summary>
    /// <param name="accountIds">The accounts of the rows being shown.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The ids of the accounts that are suspended.</returns>
    public async Task<IReadOnlySet<Guid>> SuspendedAmongAsync(
        IEnumerable<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var suspended = new HashSet<Guid>();
        foreach (var accountId in accountIds.Distinct())
        {
            var account = await _accounts.FindAsync(accountId, cancellationToken);
            if (account is { State: AccountState.Suspended })
            {
                suspended.Add(accountId);
            }
        }

        return suspended;
    }

    /// <summary>Reads how a mailbox reads given its own switch and its account's state.</summary>
    /// <param name="enabled">Whether the owner has the mailbox switched on.</param>
    /// <param name="accountSuspended">Whether the mailbox's account is suspended.</param>
    /// <returns>The reading.</returns>
    public static MailboxStatus MailboxStatusOf(bool enabled, bool accountSuspended)
    {
        if (accountSuspended)
        {
            return MailboxStatus.Suspended;
        }

        return enabled ? MailboxStatus.Active : MailboxStatus.Disabled;
    }

    /// <summary>Reads how a domain reads given its account's state.</summary>
    /// <param name="accountSuspended">Whether the domain's account is suspended.</param>
    /// <returns>The reading.</returns>
    public static MailDomainStatus DomainStatusOf(bool accountSuspended)
    {
        return accountSuspended ? MailDomainStatus.Suspended : MailDomainStatus.Active;
    }
}
