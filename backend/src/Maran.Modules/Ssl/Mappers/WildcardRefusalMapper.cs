using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Mappers;

/// <summary>Restates the reason a wildcard cannot be ordered as the typed failure a caller is answered with.</summary>
public static class WildcardRefusalMapper
{
    /// <summary>Maps an availability that is not <see cref="WildcardAvailability.Available"/> to its failure.</summary>
    /// <param name="availability">What stood in the way.</param>
    /// <returns>The error, code and kind together.</returns>
    public static Error From(WildcardAvailability availability)
    {
        return availability switch
        {
            WildcardAvailability.FeatureNotIncluded =>
                Error.Of(nameof(ErrorMessages.WildcardFeatureNotIncluded), ErrorType.Conflict),
            WildcardAvailability.DnsModuleAbsent =>
                Error.Of(nameof(ErrorMessages.WildcardDnsNotAvailable), ErrorType.Conflict),
            WildcardAvailability.DnsNotEnabled =>
                Error.Of(nameof(ErrorMessages.WildcardDnsNotEnabled), ErrorType.Conflict),
            WildcardAvailability.ZoneNotServed =>
                Error.Of(nameof(ErrorMessages.WildcardZoneNotServed), ErrorType.Conflict),
            _ => throw new ArgumentOutOfRangeException(
                nameof(availability), availability, "An available wildcard has no refusal."),
        };
    }
}
