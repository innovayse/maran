using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountFeatureDirectory"/> double that answers from one set of features for every account
/// and remembers which accounts it was asked about.
/// </summary>
/// <remarks>
/// The accounts asked are what a test needs to see: the wildcard check must ask about the OWNER of the site,
/// and an answer that did not depend on the account would hide a check made about the wrong one.
/// </remarks>
public sealed class StubAccountFeatureDirectory : IAccountFeatureDirectory
{
    /// <summary>The features every account's plan includes.</summary>
    public AccountFeature Included { get; set; } = AccountFeature.All;

    /// <summary>Every account the directory was asked about, in order.</summary>
    public List<Guid> AccountsAsked { get; } = [];

    /// <inheritdoc />
    public Task<bool> IncludesAsync(Guid accountId, AccountFeature feature, CancellationToken cancellationToken)
    {
        AccountsAsked.Add(accountId);

        return Task.FromResult((Included & feature) == feature);
    }
}
