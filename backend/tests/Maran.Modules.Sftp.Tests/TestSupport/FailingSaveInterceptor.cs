using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// Makes the next <c>SaveChangesAsync</c> on a context throw a chosen exception, so a test can kill
/// the row insert of a create that has already reached the agent.
/// </summary>
/// <remarks>
/// An EF Core interceptor rather than a subclassed context, because <c>SftpDbContext</c> is
/// sealed and must stay sealed: unsealing production code so a test can override a method makes the
/// test's needs visible in the shipped type, and the next reader cannot tell an extension point from
/// a concession. An interceptor is EF's own supported seam and touches nothing.
///
/// It throws on the way IN, before anything is written, which is exactly the shape of the failure
/// being reproduced: the agent has created the login and the panel's row does not exist.
/// </remarks>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>The failure to raise, or null once it has been spent.</summary>
    private Exception? _failure;

    /// <summary>Arms the interceptor with the exception the next save will throw.</summary>
    /// <param name="failure">What the next save fails with.</param>
    public FailingSaveInterceptor(Exception failure)
    {
        _failure = failure;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Spent after one throw, so a handler that compensates and then saves something else is not
        // stopped by a fixture that was only meant to break one write.
        var failure = _failure;
        _failure = null;

        if (failure is not null)
        {
            throw failure;
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
