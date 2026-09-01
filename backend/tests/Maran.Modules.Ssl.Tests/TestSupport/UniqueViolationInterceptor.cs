using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An EF Core interceptor that makes the first save throw <see cref="DbUpdateException"/>, standing
/// in for the unique index refusing a duplicate domain.
/// </summary>
/// <remarks>
/// Necessary because the in-memory provider enforces no unique constraint at all, so the race this
/// exists to test — two requests both passing the duplicate check, the database refusing the second
/// insert — cannot happen there on its own. Driving it through an interceptor keeps the test on the
/// production entry point: the handler still runs its own check, still calls the authority, still
/// installs, and still reaches its own catch.
/// </remarks>
public sealed class UniqueViolationInterceptor : SaveChangesInterceptor
{
    /// <summary>How many more saves should throw.</summary>
    private int _remaining;

    /// <summary>Creates an interceptor that fails the next <paramref name="failures"/> saves.</summary>
    /// <param name="failures">How many saves should throw before they start succeeding.</param>
    public UniqueViolationInterceptor(int failures)
    {
        _remaining = failures;
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
            throw new DbUpdateException("duplicate key value violates unique constraint IX_Certificates_Domain");
        }

        return ValueTask.FromResult(result);
    }
}
