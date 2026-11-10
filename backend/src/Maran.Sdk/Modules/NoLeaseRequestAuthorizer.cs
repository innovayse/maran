using Maran.Sdk.Interfaces;

namespace Maran.Sdk.Modules;

/// <summary>
/// The <see cref="ILeaseRequestAuthorizer"/> the panel runs with until the Licensing module holds a
/// lease: it attaches nothing. A request therefore goes out anonymously, which is what a panel with no
/// lease is entitled to - the public catalogue, browse only.
/// </summary>
public sealed class NoLeaseRequestAuthorizer : ILeaseRequestAuthorizer
{
    /// <inheritdoc />
    public Task<bool> TryAuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        return Task.FromResult(false);
    }
}
