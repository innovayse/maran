using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>Makes every save of a context fail with a given exception, standing in for a database that refuses the write.</summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>The failure every save raises.</summary>
    private readonly DbUpdateException _failure;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="failure">The failure every save raises.</param>
    public FailingSaveInterceptor(DbUpdateException failure)
    {
        _failure = failure;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        throw _failure;
    }
}
