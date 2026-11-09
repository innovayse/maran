using System.Collections.Concurrent;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// The ids of the accounts, resellers and plans that hold a demo login or give it its screens.
/// The seeder fills it after seeding; the guard refuses to suspend, delete, transfer or re-plan them.
/// </summary>
/// <remarks>
/// An empty set silently allows everything it guards, so the seeder owes a call to <see cref="Protect"/>
/// for every seeded identity-bearing resource and the integration tests read the set back. The set also
/// says whether it was ever filled (<see cref="IsLoaded"/>): until it was, the guard cannot tell a
/// protected resource from any other and refuses every action that depends on the answer.
/// </remarks>
public sealed class DemoProtectedResources
{
    /// <summary>The protected ids; the value is unused, the dictionary standing in for a concurrent set.</summary>
    private readonly ConcurrentDictionary<Guid, bool> _ids = new();

    /// <summary>Set once the loader has filled the set from a seeded database; read by many threads.</summary>
    private volatile bool _loaded;

    /// <summary>
    /// Whether the set was filled from a seeded database. False at start and after nothing was found: the
    /// guard then fails closed instead of allowing everything an empty set would let through.
    /// </summary>
    public bool IsLoaded
    {
        get { return _loaded; }
    }

    /// <summary>How many resources are protected.</summary>
    public int Count
    {
        get { return _ids.Count; }
    }

    /// <summary>Every protected id, for diagnostics and for tests that need one.</summary>
    public IReadOnlyCollection<Guid> Ids
    {
        get { return _ids.Keys.ToList(); }
    }

    /// <summary>Records that the set now holds every protected resource the database has.</summary>
    public void MarkLoaded()
    {
        _loaded = true;
    }

    /// <summary>Marks a resource as protected.</summary>
    /// <param name="id">The resource id.</param>
    public void Protect(Guid id)
    {
        _ids[id] = true;
    }

    /// <summary>Whether a resource is protected.</summary>
    /// <param name="id">The resource id.</param>
    /// <returns><c>true</c> when the seed marked it.</returns>
    public bool IsProtected(Guid id)
    {
        return _ids.ContainsKey(id);
    }
}
