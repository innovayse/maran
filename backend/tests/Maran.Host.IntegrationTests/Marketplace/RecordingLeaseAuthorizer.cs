using System.Net.Http.Headers;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// A licence-lease authorizer that always has a lease: it attaches a known bearer token to every request
/// it is asked about. The token is the planted secret of the leak tests, so a response, log line or audit
/// entry that repeats it is found by a plain text search.
/// </summary>
public sealed class RecordingLeaseAuthorizer : ILeaseRequestAuthorizer
{
    /// <summary>How many requests the transport asked this authorizer about.</summary>
    private int _calls;

    /// <summary>The bearer token this authorizer attaches.</summary>
    public string Token { get; }

    /// <summary>How many requests the transport asked this authorizer about.</summary>
    public int Calls
    {
        get
        {
            return Volatile.Read(ref _calls);
        }
    }

    /// <summary>Creates the authorizer.</summary>
    /// <param name="token">The bearer token to attach.</param>
    public RecordingLeaseAuthorizer(string token)
    {
        Token = token;
    }

    /// <inheritdoc />
    public Task<bool> TryAuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        return Task.FromResult(true);
    }
}
