using System.Net.Http.Headers;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>An authorizer that attaches a fixed bearer value when the test says the panel holds a lease, and counts its calls.</summary>
public sealed class StubLeaseAuthorizer : ILeaseRequestAuthorizer
{
    /// <summary>Whether the panel holds a lease.</summary>
    public bool HasLease { get; set; }

    /// <summary>How many times the transport asked.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<bool> TryAuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;

        if (HasLease)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-lease");
        }

        return Task.FromResult(HasLease);
    }
}
