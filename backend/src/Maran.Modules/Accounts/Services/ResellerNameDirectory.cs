using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Names the reseller that owns an account or a plan, so a screen reads "Acme Hosting" and not a
/// GUID (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// It reads through the context's own narrowing, deliberately: a reseller asking about its own
/// accounts is shown its own name, and an id that names a reseller the caller may not see reads as no
/// name at all. It never decides who owns a row; it only restates an owner id as the owner's name.
/// </remarks>
public sealed class ResellerNameDirectory
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public ResellerNameDirectory(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Names one reseller.</summary>
    /// <param name="resellerId">The owner id a row carries, or <c>null</c> for the administrator's own.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reseller's name, or <c>null</c> for the administrator's own or a reseller the caller may not see.</returns>
    public async Task<string?> NameOfAsync(Guid? resellerId, CancellationToken cancellationToken)
    {
        if (resellerId is not { } id)
        {
            return null;
        }

        var names = await NamesOfAsync([id], cancellationToken);
        return names.GetValueOrDefault(id);
    }

    /// <summary>Names a set of resellers in one read.</summary>
    /// <param name="resellerIds">The owner ids of the rows being shown; a row owned by the administrator has none to ask about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The name of each reseller the caller may see, by id; absent for any other.</returns>
    public async Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> resellerIds,
        CancellationToken cancellationToken)
    {
        var requested = resellerIds.Distinct().ToList();
        if (requested.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // The selectors are delegates run after the read, not translated, so they are braced.
        return await _dbContext.Resellers
            .AsNoTracking()
            .Where(reseller => requested.Contains(reseller.Id))
            .ToDictionaryAsync(
                reseller =>
                {
                    return reseller.Id;
                },
                reseller =>
                {
                    return reseller.Name;
                },
                cancellationToken);
    }
}
