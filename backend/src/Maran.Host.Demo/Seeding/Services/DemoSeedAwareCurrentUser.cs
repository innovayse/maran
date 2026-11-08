using Maran.Host.Security;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// The demonstration's <see cref="ICurrentUser"/>: the caller the seed is acting as while it seeds,
/// and the request's own caller at every other time.
/// </summary>
public sealed class DemoSeedAwareCurrentUser : ICurrentUser
{
    /// <summary>Holds the caller the seed acts as.</summary>
    private readonly DemoSeedCallerAccessor _seed;

    /// <summary>Reads the caller of a served request.</summary>
    private readonly HttpContextCurrentUser _request;

    /// <summary>Creates the current user.</summary>
    /// <param name="seed">Holds the caller the seed acts as.</param>
    /// <param name="request">Reads the caller of a served request.</param>
    public DemoSeedAwareCurrentUser(DemoSeedCallerAccessor seed, HttpContextCurrentUser request)
    {
        _seed = seed;
        _request = request;
    }

    /// <inheritdoc />
    public Caller Caller
    {
        get { return _seed.Current ?? _request.Caller; }
    }
}
