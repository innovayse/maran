using Maran.Modules.Ssl.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Ssl.IntegrationEvents.Handlers;

/// <summary>
/// Removes this module's rows for an account that is about to be deleted
/// (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Nothing removed a <c>Certificate</c> row when its account was
/// deleted, so a live browser run found one still listed against an account the panel no longer had
/// — with an expiry date the renewal job would keep reading, for a domain nobody owned any more.
/// </para>
/// <para>
/// <b>This removes ROWS, not material, and here that is a division of labour rather than a rule of
/// thumb.</b> Certificate material is stored per DOMAIN and taken away by the agent's own site
/// deletion, immediately after the vhost goes, because unlinking a key the running configuration
/// still names makes the next <c>nginx -t</c> fail — possibly for an unrelated site, minutes later.
/// The Sites module's subscriber deletes every one of the account's sites through that rpc, so the
/// material of every certificate reachable from this table is already gone by the time these rows
/// are. Removing it a second time from here would mean this handler re-rendering vhosts for sites
/// that are being deleted in the same cascade, and the order of two subscribers to one message is
/// not something either of them may depend on.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — the recoverable half.
/// </para>
/// <para>
/// <b><c>AcmeAccount</c> is deliberately untouched.</b> It is the server's registration with the
/// certificate authority, server-wide and carrying no <c>AccountId</c> at all. A hosting account
/// being deleted is no reason to throw away the key the panel renews every other customer's
/// certificates with.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    public AccountDeletingHandler(SslDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Deletes every certificate row belonging to the account being removed.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed, for the reason the Databases module's
    /// handler sets out: the filter governs what a REQUEST may see, and this is the removal of the
    /// account the rows belong to, authorised before the event was published.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.Certificates
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        _dbContext.Certificates.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
