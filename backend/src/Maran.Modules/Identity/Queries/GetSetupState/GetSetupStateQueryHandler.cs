using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.GetSetupState;

/// <summary>Handles <see cref="GetSetupStateQuery"/> by asking whether any user exists.</summary>
public sealed class GetSetupStateQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetSetupStateQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reports whether setup is finished.</summary>
    /// <param name="query">The (empty) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// A successful result carrying the state. It reveals only that the panel has an owner, which a
    /// visitor learns anyway from being shown a login screen.
    /// </returns>
    public async Task<Result<SetupStateDto>> HandleAsync(GetSetupStateQuery query, CancellationToken cancellationToken)
    {
        return Result<SetupStateDto>.Ok(new SetupStateDto(await _dbContext.Users.AnyAsync(cancellationToken)));
    }
}
