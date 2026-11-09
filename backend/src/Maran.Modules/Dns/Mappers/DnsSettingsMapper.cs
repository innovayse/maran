using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates <see cref="DnsSettings"/> as the wire shape the administrator's screen reads.</summary>
public static class DnsSettingsMapper
{
    /// <summary>Maps the settings row.</summary>
    /// <param name="settings">The row.</param>
    /// <returns>The wire shape.</returns>
    public static DnsSettingsDto From(DnsSettings settings)
    {
        return new DnsSettingsDto(
            settings.PrimaryNameserver,
            settings.SecondaryNameserver,
            settings.HostmasterMailbox,
            settings.PublicIpv4,
            settings.PublicIpv6,
            settings.DefaultTtl,
            settings.Enabled,
            settings.IsComplete);
    }
}
