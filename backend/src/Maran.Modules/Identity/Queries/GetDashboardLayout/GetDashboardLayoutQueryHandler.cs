using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.GetDashboardLayout;

/// <summary>Handles <see cref="GetDashboardLayoutQuery"/> by combining the caller's choices with the defaults.</summary>
public sealed class GetDashboardLayoutQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetDashboardLayoutQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the effective layout of the caller.</summary>
    /// <param name="query">Whose layout, and which defaults it starts from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The two lists in drawing order. Only the caller's own rows are ever read, so there is no other
    /// person's layout to answer 404 for; a person who never saved anything gets the defaults.
    /// </returns>
    public async Task<Result<DashboardLayoutDto>> HandleAsync(
        GetDashboardLayoutQuery query,
        CancellationToken cancellationToken)
    {
        var stored = await _dbContext.DashboardPreferences
            .AsNoTracking()
            .Where(preference => preference.UserId == query.UserId)
            .ToListAsync(cancellationToken);

        return Result<DashboardLayoutDto>.Ok(DashboardLayoutMapper.From(
            DashboardLayoutPolicy.Effective(stored, DashboardPreferenceKind.Widget, query.ForAdministrator),
            DashboardLayoutPolicy.Effective(stored, DashboardPreferenceKind.Action, query.ForAdministrator)));
    }
}
