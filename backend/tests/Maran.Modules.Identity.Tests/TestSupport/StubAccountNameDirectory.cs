using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountNameDirectory"/> answering with exactly the names a test registered, and
/// recording which ids it was asked about so a test can assert that only the caller's own
/// memberships ever reached it.
/// </summary>
public sealed class StubAccountNameDirectory : IAccountNameDirectory
{
    /// <summary>The accounts this directory knows about, by id.</summary>
    private readonly Dictionary<Guid, string> _names = [];

    /// <summary>Every id the handler asked for, across all calls.</summary>
    public List<Guid> Asked { get; } = [];

    /// <summary>Registers an account name.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="name">The account's name.</param>
    public void Add(Guid accountId, string name)
    {
        _names[accountId] = name;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        Asked.AddRange(accountIds);

        IReadOnlyDictionary<Guid, string> found = accountIds
            .Distinct()
            .Where(id => { return _names.ContainsKey(id); })
            .ToDictionary(id => { return id; }, id => { return _names[id]; });

        return Task.FromResult(found);
    }
}
