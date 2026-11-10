using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Reads the name a login is known by, for the journal entries whose subject is the login itself: a
/// sign-out acts on the person who signed out, and an entry that printed their id would be unreadable.
/// </summary>
public sealed class LoginNameReader
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public LoginNameReader(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads one login's name.</summary>
    /// <param name="userId">The login's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The name, or an empty string when the login is gone or has been erased: the journal's subject is
    /// then blank, which is a gap, and never the identifier of an erased person.
    /// </returns>
    public async Task<string> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var name = await _dbContext.Users
            .AsNoTracking()
            .Where(login => login.Id == userId)
            .Select(login => login.Username)
            .SingleOrDefaultAsync(cancellationToken);

        return name ?? string.Empty;
    }
}
