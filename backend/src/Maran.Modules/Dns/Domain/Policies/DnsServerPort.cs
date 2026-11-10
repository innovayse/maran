namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The port the name server answers on, written once so the status can hand it to the screen. The screen
/// holds no port of its own (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public static class DnsServerPort
{
    /// <summary>
    /// 53, for both TCP and UDP. The agent binds and probes the same number; switching the daemon on does not
    /// open it in the firewall, so this is the port the administrator is shown and asked to open.
    /// </summary>
    public const int Number = 53;
}
