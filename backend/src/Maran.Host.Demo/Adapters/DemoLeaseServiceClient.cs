using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// The demo's <see cref="ILeaseServiceClient"/>: it answers "no licence service" without creating an HTTP
/// client or sending a byte. A visitor can install the demonstration's own licence, and the real client would
/// then ask the production licence service about it from a public demonstration host.
/// </summary>
public sealed class DemoLeaseServiceClient : ILeaseServiceClient
{
    /// <inheritdoc />
    public Task<LeaseServiceAnswer> RequestAsync(string licenceId, CancellationToken cancellationToken)
    {
        return Task.FromResult(LeaseServiceAnswer.Without(LeaseServiceAnswerKind.NotConfigured));
    }
}
