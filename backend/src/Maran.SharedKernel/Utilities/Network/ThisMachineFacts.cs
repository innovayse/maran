using System.Net;
using System.Net.NetworkInformation;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// What names this machine: the address of every one of its network interfaces and its own host name.
/// </summary>
/// <param name="Addresses">Every unicast address of every interface, IPv4-mapped forms unwrapped, zone ids stripped.</param>
/// <param name="Names">The machine's host name and its fully qualified form, lower-case, without a trailing dot.</param>
/// <remarks>
/// <b>A snapshot.</b> An address that appears later (a second IP, a rotated temporary address) is not in
/// a value already read, so a caller reads through <see cref="Interfaces.IThisMachineFactsSource"/> at every use
/// instead of keeping one; the check built on it (<see cref="ThisMachineRule"/> for a settings form) is
/// advice that errs permissive, because the mail server's own refusal of the machine's addresses is the
/// enforcement.
/// </remarks>
public sealed record ThisMachineFacts(IReadOnlyCollection<IPAddress> Addresses, IReadOnlyCollection<string> Names)
{
    /// <summary>The facts of a machine that knows nothing about itself, which leaves only loopback and <c>localhost</c> recognised.</summary>
    public static ThisMachineFacts None { get; } = new([], []);

    /// <summary>Reads the interfaces and the host name of this machine.</summary>
    /// <returns>The facts; empty collections when the operating system refuses to say.</returns>
    public static ThisMachineFacts Read()
    {
        var addresses = new List<IPAddress>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    addresses.Add(ScopelessAddress.Strip(Ipv4MappedAddress.Unwrap(unicast.Address)));
                }
            }

            var properties = IPGlobalProperties.GetIPGlobalProperties();
            AddName(names, properties.HostName);
            if (!string.IsNullOrEmpty(properties.DomainName))
            {
                AddName(names, string.Concat(properties.HostName, ".", properties.DomainName));
            }
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
        {
            // The facts are advice for a settings form: a machine that cannot list itself (the system
            // reports a failure, or the platform has no such interface listing) is treated as knowing
            // only loopback, and the mail server's own refusal still stands.
        }

        return new ThisMachineFacts(addresses, names);
    }

    /// <summary>Adds one name in its comparison form.</summary>
    /// <param name="names">The set being built.</param>
    /// <param name="name">The name as the system reported it.</param>
    private static void AddName(HashSet<string> names, string name)
    {
        var trimmed = (name ?? string.Empty).Trim().TrimEnd('.');
        if (trimmed.Length > 0)
        {
            names.Add(trimmed);
        }
    }
}
