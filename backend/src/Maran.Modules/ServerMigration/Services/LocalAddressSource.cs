using System.Net;
using System.Net.NetworkInformation;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>Reads this server's own addresses from its network interfaces (issue #127). The real <see cref="ILocalAddressSource"/>.</summary>
public sealed class LocalAddressSource : ILocalAddressSource
{
    /// <inheritdoc />
    public IReadOnlySet<IPAddress> Read()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(networkInterface =>
            {
                return networkInterface.GetIPProperties().UnicastAddresses;
            })
            .Select(unicast =>
            {
                return Ipv4MappedAddress.Unwrap(unicast.Address);
            })
            .ToHashSet();
    }
}
