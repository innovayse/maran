using System.ComponentModel.DataAnnotations;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Options;

/// <summary>
/// The DNS module's operator settings, bound from the <c>Dns</c> configuration section and validated at
/// start-up (rules/csharp.md "Options validated at startup"). A bound an operator may tune; the bounds the
/// protocol fixes are <c>DnsLimits</c>.
/// </summary>
public sealed class DnsOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Dns";

    /// <summary>
    /// The most records one zone may hold. Every accepted edit re-renders the whole zone on the root agent
    /// and reloads it, so this bounds that work and the size of the file the daemon parses. Not a plan
    /// dimension: it protects the server, and a plan sells how many zones, not how large the daemon's
    /// workload may grow.
    /// </summary>
    [Range(1, DnsLimits.MaxRecordsPerZoneCeiling)]
    public int MaxRecordsPerZone { get; set; } = 500;
}
