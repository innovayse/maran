using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An EF Core interceptor that makes the first save throw <see cref="DbUpdateException"/>, standing
/// in for the database refusing the write — by default for a duplicate domain.
/// </summary>
/// <remarks>
/// Necessary because the in-memory provider enforces no unique constraint at all, so the race this
/// exists to test — two requests both passing the duplicate check, the database refusing the second
/// insert — cannot happen there on its own. Driving it through an interceptor keeps the test on the
/// production entry point: the handler still runs its own check, still calls the authority, still
/// installs, and still reaches its own catch.
///
/// The exception carries a real <see cref="PostgresException"/> as its inner exception, with a
/// SQLSTATE, because the handler distinguishes a unique violation (where a winning row exists) from
/// every other database failure (where none does) — and an interceptor that threw a bare
/// <see cref="DbUpdateException"/> would let a handler that could not tell them apart pass.
/// </remarks>
public sealed class UniqueViolationInterceptor : SaveChangesInterceptor
{
    /// <summary>The SQLSTATE the simulated failure carries.</summary>
    private readonly string _sqlState;

    /// <summary>How many more saves should throw.</summary>
    private int _remaining;

    /// <summary>Creates an interceptor that fails the next <paramref name="failures"/> saves.</summary>
    /// <param name="failures">How many saves should throw before they start succeeding.</param>
    /// <param name="sqlState">The SQLSTATE to fail with; a unique violation unless a test wants another failure.</param>
    public UniqueViolationInterceptor(int failures, string sqlState = PostgresErrorCodes.UniqueViolation)
    {
        _remaining = failures;
        _sqlState = sqlState;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_remaining > 0)
        {
            _remaining--;
            const string Message = "duplicate key value violates unique constraint IX_Certificates_Domain";
            throw new DbUpdateException(Message, new PostgresException(Message, "ERROR", "ERROR", _sqlState));
        }

        return ValueTask.FromResult(result);
    }
}
