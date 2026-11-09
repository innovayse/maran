using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>An <see cref="ISiteHostnameDirectory"/> answering for exactly the claims a test registered.</summary>
public sealed class StubSiteHostnameDirectory : ISiteHostnameDirectory
{
    /// <summary>The (account, host name) pairs that are claimed.</summary>
    private readonly HashSet<(Guid AccountId, string Hostname)> _claims = [];

    /// <summary>Registers that an account's site serves a host name.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="hostname">The host name.</param>
    public void Claim(Guid accountId, string hostname)
    {
        _claims.Add((accountId, hostname));
    }

    /// <inheritdoc />
    public Task<bool> AccountClaimsHostnameAsync(Guid accountId, string hostname, CancellationToken cancellationToken)
    {
        return Task.FromResult(_claims.Contains((accountId, hostname)));
    }
}
