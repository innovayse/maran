using System.Diagnostics;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Decides which entries of the audit journal the current caller may read (issue #370): every entry
/// for an administrator, the entries about its own accounts and its own login for a reseller, and
/// nothing — a refusal — for every other caller.
/// </summary>
/// <remarks>
/// <para>
/// <b>It fails closed.</b> The controllers' policies already turn the wrong role away, and this is
/// the second line: a reader of the journal that was dispatched by a caller neither case names (an
/// API key, a customer, unattended work with no principal) is refused here instead of being handed
/// the whole journal because nobody remembered to narrow it.
/// </para>
/// <para>
/// The reseller's accounts are read from the Accounts module through <see cref="IResellerDirectory"/>,
/// which takes the reseller from the caller's own token. This type never accepts a reseller id from a
/// request, so a reseller cannot widen its scope by naming another.
/// </para>
/// </remarks>
public sealed class AuditEventScopeReader
{
    /// <summary>The authenticated principal whose scope is decided.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The Accounts module's window onto the accounts a reseller owns.</summary>
    private readonly IResellerDirectory _resellers;

    /// <summary>Creates the reader.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="resellers">The window onto the accounts a reseller owns.</param>
    public AuditEventScopeReader(ICurrentUser currentUser, IResellerDirectory resellers)
    {
        _currentUser = currentUser;
        _resellers = resellers;
    }

    /// <summary>Decides the caller's scope.</summary>
    /// <param name="cancellationToken">Cancels the read of the reseller's accounts.</param>
    /// <returns>
    /// The scope, or <c>AuditJournalNotReadable</c> when the caller is neither an administrator nor a reseller.
    /// </returns>
    public async Task<Result<AuditEventScope>> ReadAsync(CancellationToken cancellationToken)
    {
        switch (_currentUser.Caller)
        {
            case AdministratorCaller:
                return Result<AuditEventScope>.Ok(new AllEntriesScope());
            case ResellerCaller reseller:
                var ownedAccountIds = await _resellers.ListOwnedAccountIdsAsync(cancellationToken);
                return Result<AuditEventScope>.Ok(new ResellerEntriesScope(ownedAccountIds, reseller.UserId));
            case CustomerCaller:
            case AccountlessCustomerCaller:
            case ApiKeyCaller:
            case AnonymousCaller:
                return Result<AuditEventScope>.Fail(
                    Error.Of(nameof(ErrorMessages.AuditJournalNotReadable), ErrorType.Forbidden));
            default:
                throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist.");
        }
    }
}
