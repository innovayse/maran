using System.Net;
using System.Net.Sockets;
using Maran.Modules.ServerMigration.Interfaces;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>The <see cref="IDomainResolver"/> over the system resolver, bounded in time.</summary>
public sealed class SystemDomainResolver : IDomainResolver
{
    /// <summary>The longest one lookup may take.</summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domain);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(LookupTimeout);

        try
        {
            return await Dns.GetHostAddressesAsync(domain, bounded.Token);
        }
        catch (SocketException)
        {
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
