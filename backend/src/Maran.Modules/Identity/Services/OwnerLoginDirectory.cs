using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Tells the Accounts module whether the owner login of a new account can be made, from the login table
/// itself. Compares exactly as the unique indexes <c>IX_Users_Username</c> and <c>IX_Users_Email</c> do,
/// so the question and the constraint cannot disagree about what "the same" means.
/// </summary>
public sealed class OwnerLoginDirectory : IOwnerLoginDirectory
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public OwnerLoginDirectory(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<OwnerLoginConflict> FindConflictAsync(
        string username,
        string email,
        CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.AnyAsync(login => login.Username == username, cancellationToken))
        {
            return OwnerLoginConflict.UsernameTaken;
        }

        return await _dbContext.Users.AnyAsync(login => login.Email == email, cancellationToken)
            ? OwnerLoginConflict.EmailTaken
            : OwnerLoginConflict.None;
    }
}
