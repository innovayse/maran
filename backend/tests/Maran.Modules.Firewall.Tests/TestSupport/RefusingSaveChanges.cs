using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// An interceptor that makes every save fail the way PostgreSQL does, by default as its unique index
/// does, so a test can drive the one path the in-memory provider cannot produce on its own.
/// </summary>
/// <remarks>
/// The in-memory provider enforces no unique index, so two concurrent deliveries of one detection
/// cannot be made to collide in a unit test at all — and the collision is exactly what a handler
/// documented as never throwing has to survive. Standing in for the refusal is the honest way to
/// assert the behaviour: what matters is that a <see cref="DbUpdateException"/> caused by a unique
/// violation out of <c>SaveChangesAsync</c> does not escape the handler, and this produces one. A
/// different SqlState produces the failure that must escape.
/// </remarks>
public sealed class RefusingSaveChanges : ISaveChangesInterceptor
{
    /// <summary>The message the refusal carries, so a reader of a failing test knows it was staged.</summary>
    private const string Message = "The database refused this row (staged by RefusingSaveChanges)";

    /// <summary>The SqlState the staged failure carries.</summary>
    private readonly string _sqlState;

    /// <summary>Creates an interceptor that refuses every save with the given SqlState.</summary>
    /// <param name="sqlState">The SqlState of the staged failure; the unique violation when omitted.</param>
    public RefusingSaveChanges(string sqlState = PostgresErrorCodes.UniqueViolation)
    {
        _sqlState = sqlState;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        throw new DbUpdateException(
            Message,
            new PostgresException(Message, "ERROR", "ERROR", _sqlState));
    }
}
