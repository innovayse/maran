using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Writes audit entries into <c>identity.AuditEvents</c>, stamping each with the panel's clock and
/// the request's correlation id so a journal row and the log lines around it can be read together.
/// </summary>
/// <remarks>
/// The type has no update or delete method, and neither does <see cref="AuditEvent"/>. That absence
/// is what "append-only" means here — enforced by the shape of the code rather than by a convention
/// someone has to remember (spec §10).
/// </remarks>
public sealed class DatabaseAuditWriter : IAuditWriter
{
    /// <summary>The module's database context; Identity owns the journal's table.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The current request's correlation id, tying a row to the logs.</summary>
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>Creates the writer.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="correlationIdAccessor">The current request's correlation id.</param>
    public DatabaseAuditWriter(
        IdentityDbContext dbContext,
        IClock clock,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _dbContext = dbContext;
        _clock = clock;
        _correlationIdAccessor = correlationIdAccessor;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            _clock.UtcNow,
            entry.ActorUserId,
            entry.ActorUsername,
            entry.Action,
            entry.Subject,
            entry.IpAddress,
            entry.UserAgent,
            entry.Succeeded,
            _correlationIdAccessor.CorrelationId));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
