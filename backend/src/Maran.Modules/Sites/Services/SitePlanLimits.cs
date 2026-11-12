using Maran.Modules.Sites.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Reads the plan's PHP bounds of an account for work that acts on the account whoever asked for it
/// and so has no tenant to scope by (issue #556).
/// </summary>
/// <remarks>
/// <para>
/// A vhost carries a request body limit that follows the plan (<c>SiteBodySizePolicy</c>), so every path
/// that renders one needs the bounds. A request path already holds the account it read through the
/// tenant-scoped window and uses its <see cref="AccountSnapshot.PhpLimits"/>. The paths that have no
/// principal read them here: the account cascades (suspension and resumption), the queued render of an
/// account's vhosts after a plan edit, and the site directory the Ssl module's unattended renewal reads through.
/// </para>
/// <para>
/// The figures are used to decide a vhost and are never handed back to a caller, which is why the unscoped
/// window may be read at all; this file is its one caller in the Sites module (the census names it).
/// </para>
/// </remarks>
public sealed class SitePlanLimits
{
    /// <summary>The account snapshot window, read without a tenant scope.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the reader.</summary>
    /// <param name="accounts">The account snapshot window.</param>
    public SitePlanLimits(IAccountDirectory accounts)
    {
        _accounts = accounts;
    }

    /// <summary>Reads one account's whole snapshot, for the background pass that needs its system user too.</summary>
    /// <param name="accountId">The account whose sites are rendered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or null when the account does not exist.</returns>
    public async Task<AccountSnapshot?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _accounts.FindForSystemAsync(accountId, cancellationToken);
    }

    /// <summary>Reads the plan's PHP bounds of one account.</summary>
    /// <param name="accountId">The account that owns the site being rendered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The account's PHP bounds, or <c>AccountNotFound</c> when the account does not exist. A site row
    /// without its account is a broken invariant (the account's deletion removes its sites), but the caller
    /// decides what that means for it, so it is answered and not thrown from a reader.
    /// </returns>
    public async Task<Result<PhpLimitsSnapshot>> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(accountId, cancellationToken);

        return account is null
            ? Result<PhpLimitsSnapshot>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound))
            : Result<PhpLimitsSnapshot>.Ok(account.PhpLimits);
    }
}
