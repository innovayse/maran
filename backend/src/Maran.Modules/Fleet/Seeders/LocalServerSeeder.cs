using Maran.Modules.Fleet.Domain.Entities;
using Maran.Modules.Fleet.Persistence;
using Maran.SharedKernel.Constants;

namespace Maran.Modules.Fleet.Seeders;

/// <summary>
/// Makes sure the server the panel runs on has its row in the registry, so a panel with no enrolled
/// node still has exactly one server and everything that names a server has an answer.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent and cheap: it reads one row by its well-known identity and inserts nothing when it is
/// there. It never rewrites an existing row, so an administrator's rename of the local server survives
/// every restart; the name passed in is only the one a NEW row starts with.
/// </para>
/// <para>
/// Two panel processes starting together can both find the row missing; the primary key on the fixed
/// identity lets one insert win, and the loser reads the winner's row and returns, rather than failing
/// the start-up over a row that now exists.
/// </para>
/// </remarks>
public sealed class LocalServerSeeder
{
    /// <summary>The Fleet module's database context.</summary>
    private readonly FleetDbContext _dbContext;

    /// <summary>The source of the creation time.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seeder.</summary>
    /// <param name="dbContext">The Fleet module's database context.</param>
    /// <param name="clock">The time source; the ambient clock is banned.</param>
    public LocalServerSeeder(FleetDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Inserts the local server's row when it is missing.</summary>
    /// <param name="name">
    /// What a new row is called: the host name the panel runs on. Not blank, at most
    /// <see cref="Server.NameMaxLength"/> characters.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the read and the write.</param>
    /// <returns>True when this call inserted the row; false when it already existed.</returns>
    public async Task<bool> SeedAsync(string name, CancellationToken cancellationToken)
    {
        if (await ExistsAsync(cancellationToken))
        {
            return false;
        }

        _dbContext.Servers.Add(Server.CreateLocal(name, _clock.UtcNow));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another start-up inserted it between our read and our write; the registry is then correct.
            // Any other cause leaves the row missing, and that failure is rethrown, not swallowed.
            if (await ExistsAfterFailedInsertAsync(cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    /// <summary>Whether the local server's row is present.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>True when the row exists.</returns>
    private async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Servers.AnyAsync(server => server.Id == FleetServers.LocalId, cancellationToken);
    }

    /// <summary>Re-reads the row after a failed insert, on a clean change tracker.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>True when the row is now present, which is what makes the failed insert harmless.</returns>
    private async Task<bool> ExistsAfterFailedInsertAsync(CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        return await ExistsAsync(cancellationToken);
    }
}
