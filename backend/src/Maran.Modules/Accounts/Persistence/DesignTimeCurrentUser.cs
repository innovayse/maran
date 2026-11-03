using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Persistence;

/// <summary>
/// The <see cref="ICurrentUser"/> handed to <see cref="AccountsDbContext"/> by
/// <see cref="DesignTimeDbContextFactory"/>. Exists only so EF Core's design-time tooling can build
/// the MODEL — the shape of the tables — which it cannot do without constructing the context.
/// </summary>
/// <remarks>
/// It reports an administrator because the reseller filters must not narrow a generated migration:
/// a migration describes the table, and making that depend on a fabricated reseller id is a trap for
/// the next reader. Nothing here ever runs at runtime — the Host registers the real
/// <c>ICurrentUser</c> through DI — and no query is ever executed against this principal.
/// </remarks>
public sealed class DesignTimeCurrentUser : ICurrentUser
{
    /// <summary>
    /// The administrator reported to the context: a fixed, recognisable id (never persisted, never
    /// compared with a stored row), so the principal is a value of its own and not an empty stand-in.
    /// </summary>
    private static readonly AdministratorCaller DesignTimeAdministrator =
        new(new Guid("00000000-0000-4000-8000-00000000de51"), "design-time");

    /// <inheritdoc />
    public Caller Caller
    {
        get
        {
            return DesignTimeAdministrator;
        }
    }
}
