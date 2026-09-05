using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// An interceptor that makes every save fail the way PostgreSQL's unique index does, so a test can
/// drive the one path the in-memory provider cannot produce on its own.
/// </summary>
/// <remarks>
/// The in-memory provider enforces no unique index, so two concurrent deliveries of one detection
/// cannot be made to collide in a unit test at all — and the collision is exactly what a handler
/// documented as never throwing has to survive. Standing in for the refusal is the honest way to
/// assert the behaviour: what matters is that a <see cref="DbUpdateException"/> out of
/// <c>SaveChangesAsync</c> does not escape the handler, and this produces one.
/// </remarks>
public sealed class RefusingSaveChanges : ISaveChangesInterceptor
{
    /// <summary>The message the refusal carries, so a reader of a failing test knows it was staged.</summary>
    private const string Message = "The unique index refused this row (staged by RefusingSaveChanges)";

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        throw new DbUpdateException(Message);
    }
}
