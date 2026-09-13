using Maran.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>
/// An <see cref="IServiceScopeFactory"/> that suspends a cache load after it has read its row and
/// before it can publish it, so a test can act at the one instant the race lives in.
/// </summary>
/// <remarks>
/// <para>
/// The suspension is in the SCOPE'S <c>Dispose</c>, not in <c>CreateScope</c>, and the difference is
/// the whole value of this double. A load's <c>using var scope</c> is disposed as its method exits —
/// after the query has run and the snapshot has been projected, and before the caller assigns it to
/// the cached field. Blocking at <c>CreateScope</c> instead suspends the load BEFORE its query, so a
/// row changed while it waits is simply read fresh afterwards and every version of the cache passes:
/// that is an adjacent moment, not this one.
/// </para>
/// <para>
/// Its first version did exactly that and reported a broken cache as fixed.
/// </para>
/// </remarks>
public sealed class BlockingScopeFactory : IServiceScopeFactory, IDisposable
{
    /// <summary>The context every scope hands out.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>Set once a load has read its row and is about to publish it.</summary>
    private readonly ManualResetEventSlim _entered = new(false);

    /// <summary>Set by the test to let the suspended load continue.</summary>
    private readonly ManualResetEventSlim _release = new(false);

    /// <summary>Creates the factory.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    public BlockingScopeFactory(NotificationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Waits until a load has read its row and is suspended before publishing it.</summary>
    public void WaitUntilRowRead()
    {
        Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)), "No load reached the scope factory.");
    }

    /// <summary>Lets the suspended load carry on and publish.</summary>
    public void ReleaseLoad()
    {
        _release.Set();
    }

    /// <summary>Opens the scope a load reads through.</summary>
    /// <returns>A scope over the one context this factory owns, which suspends when disposed.</returns>
    public IServiceScope CreateScope()
    {
        return new SingleContextScope(_dbContext, _entered, _release);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _entered.Dispose();
        _release.Dispose();
    }

    /// <summary>A scope serving exactly the one context the test owns, and suspending on disposal.</summary>
    private sealed class SingleContextScope : IServiceScope, IServiceProvider
    {
        /// <summary>The context this scope serves.</summary>
        private readonly NotificationsDbContext _dbContext;

        /// <summary>Signalled when this scope is disposed, which is after the load's query.</summary>
        private readonly ManualResetEventSlim _entered;

        /// <summary>Waited on before letting the load return and publish.</summary>
        private readonly ManualResetEventSlim _release;

        /// <summary>Creates the scope.</summary>
        /// <param name="dbContext">The context to serve.</param>
        /// <param name="entered">Signalled once the load's query has finished.</param>
        /// <param name="release">Waited on before the load may publish.</param>
        public SingleContextScope(
            NotificationsDbContext dbContext,
            ManualResetEventSlim entered,
            ManualResetEventSlim release)
        {
            _dbContext = dbContext;
            _entered = entered;
            _release = release;
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider
        {
            get
            {
                return this;
            }
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(NotificationsDbContext) ? _dbContext : null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The load has read and projected its row and has not yet published it. This is the
            // instant the race lives in, so this is where the test is let in.
            //
            // The context itself is NOT disposed: it belongs to the test, and a scope that disposed
            // it here would take the store away from the assertions that follow.
            _entered.Set();
            Assert.True(_release.Wait(TimeSpan.FromSeconds(10)), "The suspended load was never released.");
        }
    }
}
