using System.Net;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>Whether a host name or address typed by an operator is this machine itself.</summary>
/// <remarks>
/// <para>
/// <b>Recognised:</b> <c>localhost</c> and every loopback address (<c>127.1</c>, <c>::1</c>, an IPv4-mapped
/// loopback, bracketed or not, with or without a trailing dot), every address of one of the machine's
/// interfaces, and the machine's own host name and fully qualified name. A name is NOT resolved: a DNS
/// name that merely points here (a vanity alias, a CNAME) is not recognised, because resolving it would
/// put a network lookup inside a form check. Whoever needs the exact answer relies on the mail server's
/// own refusal of the machine's addresses, which is the enforcement; this rule is the early, advisory
/// half and errs permissive.
/// </para>
/// <para>
/// <b>Pure.</b> The facts are passed in (<see cref="ThisMachineFacts"/>), so a test states the machine it
/// asks about instead of depending on the one it runs on.
/// </para>
/// </remarks>
public static class ThisMachineRule
{
    /// <summary>The loopback name.</summary>
    private const string Localhost = "localhost";

    /// <summary>Answers whether <paramref name="host"/> names this machine.</summary>
    /// <param name="host">What the operator typed; <c>null</c> is not this machine.</param>
    /// <param name="machine">The machine's addresses and names.</param>
    /// <returns>True when the host is loopback, one of the machine's addresses, or one of its names.</returns>
    public static bool IsThisMachine(string? host, ThisMachineFacts machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var name = (host ?? string.Empty).Trim().TrimEnd('.').Trim('[', ']');
        if (name.Length == 0)
        {
            return false;
        }

        if (name.Equals(Localhost, StringComparison.OrdinalIgnoreCase)
            || machine.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(name, out var parsed))
        {
            return false;
        }

        var address = ScopelessAddress.Strip(Ipv4MappedAddress.Unwrap(parsed));

        return IPAddress.IsLoopback(address) || machine.Addresses.Contains(address);
    }
}
