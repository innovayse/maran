using System.Diagnostics;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// What the caller's reseller identity means for a write in this module: which reseller owns what
/// the caller creates, and whether a reseller that has been suspended may still change anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>It states the caller's identity and never narrows a read.</b> Which rows a reseller sees is the
/// <see cref="AccountsDbContext"/> query filter's decision and <see cref="AccountDirectory"/>'s
/// explicit predicate; this type is for the two questions a write asks that a filter cannot answer.
/// The owner is read from the caller's own identity — the reseller's token
/// (<see cref="ResellerCaller.ResellerId"/>) or, for an integration, the API key a reseller owns
/// (<see cref="ApiKeyCaller.ResellerId"/>) — never from the request, so neither can create what another
/// reseller owns by naming its id.
/// </para>
/// <para>
/// <b>A reseller's API key is a reseller here.</b> It agrees with <see cref="AccountsDbContext"/>, which
/// narrows the same key to the same reseller's rows: a key that could read only its reseller's accounts
/// but was treated as nobody by the write floor would create for any owner it named, would not be refused
/// once its reseller was suspended, and could skip the final backup of a deletion. A key the
/// administrator owns carries no reseller and stays nobody.
/// </para>
/// <para>
/// <b>Why a suspended reseller is refused here and not only at sign-in.</b> An access token lives up
/// to fifteen minutes, and suspending a reseller ends its login through an event; the token it already
/// holds still reads as a reseller until it expires. <see cref="RequireActiveAsync"/> makes that token
/// able to read and unable to write: the state is read from the row on every write, not from the token.
/// </para>
/// </remarks>
public sealed class ResellerScope
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The authenticated principal whose reseller identity is stated.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the scope.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public ResellerScope(AccountsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The reseller the caller acts for: the id its token names for a <see cref="ResellerCaller"/>, the
    /// reseller that owns the key for an <see cref="ApiKeyCaller"/> a reseller owns, and <c>null</c> for
    /// every other case — an administrator's key included — none of which is a reseller.
    /// </summary>
    public Guid? CallerResellerId
    {
        get
        {
            return _currentUser.Caller switch
            {
                ResellerCaller reseller => reseller.ResellerId,
                AdministratorCaller => null,
                CustomerCaller => null,
                AccountlessCustomerCaller => null,
                ApiKeyCaller key => key.ResellerId,
                AnonymousCaller => null,
                _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
            };
        }
    }

    /// <summary>
    /// Decides which reseller will own what the caller creates. A reseller always creates for itself;
    /// an administrator creates for the reseller it names, or for itself when it names none.
    /// </summary>
    /// <param name="requested">The owner the request names, or <c>null</c> for none.</param>
    /// <returns>
    /// The owning reseller's id, or <c>null</c> for the administrator's own;
    /// <c>ResellerNotFound</c> when a reseller names an owner other than itself — the same answer as
    /// for a reseller that does not exist, so the id of another tenant is never confirmed.
    /// </returns>
    public Result<Guid?> ResolveOwner(Guid? requested)
    {
        if (CallerResellerId is not { } own)
        {
            return Result<Guid?>.Ok(requested);
        }

        return requested is null || requested == own
            ? Result<Guid?>.Ok(own)
            : Result<Guid?>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
    }

    /// <summary>
    /// Refuses a write by a reseller that has been suspended. Reads are never asked: a suspended
    /// reseller's live token may still look, never change.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// <c>null</c> when the caller is not a reseller or its reseller is active;
    /// <c>ResellerSuspended</c> when it is suspended; <c>ResellerNotFound</c> when the reseller no longer exists.
    /// </returns>
    public async Task<Error?> RequireActiveAsync(CancellationToken cancellationToken)
    {
        if (CallerResellerId is not { } own)
        {
            return null;
        }

        var status = await _dbContext.Resellers
            .AsNoTracking()
            .Where(reseller => reseller.Id == own)
            .Select(reseller => (ResellerStatus?)reseller.Status)
            .SingleOrDefaultAsync(cancellationToken);

        return status switch
        {
            null => Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound),
            ResellerStatus.Suspended => Error.Of(nameof(ErrorMessages.ResellerSuspended), ErrorType.Conflict),
            ResellerStatus.Active => null,
            _ => throw new UnreachableException("ResellerStatus has two members: Active and Suspended."),
        };
    }
}
