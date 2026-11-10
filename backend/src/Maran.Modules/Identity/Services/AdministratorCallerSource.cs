using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Rebuilds an administrator from the user table so a durable job can act as the person who started it
/// (issue #127). The Sdk's <see cref="IAdministratorCallerSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row is read on every ask.</b> A demoted, disabled or suspended initiator gets no caller, so a job
/// that outlives its request stops being an administrator the moment the person stops being one. Nothing here
/// is cached and nothing is read from a message.
/// </para>
/// <para>
/// <b>"May act" is the login's own definition.</b> <see cref="Domain.Entities.User.MayAuthenticate"/> is the
/// one place the panel says whether a login is usable, and this asks it rather than restating it. A locked
/// login (too many failed attempts) is a sign-in defence and does not end a job already running.
/// </para>
/// </remarks>
public sealed class AdministratorCallerSource : IAdministratorCallerSource
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the source.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AdministratorCallerSource(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<AdministratorCaller?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(login => login.Id == userId, cancellationToken);

        if (user is null || user.Role != UserRole.Admin || !user.MayAuthenticate)
        {
            return null;
        }

        return new AdministratorCaller(user.Id, user.Username);
    }
}
