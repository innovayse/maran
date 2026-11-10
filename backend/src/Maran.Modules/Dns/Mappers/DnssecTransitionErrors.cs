using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates a zone's refusal of a DNSSEC step as the typed failure a caller is answered with.</summary>
public static class DnssecTransitionErrors
{
    /// <summary>Maps a verdict that is not <see cref="DnssecTransitionVerdict.Accepted"/> to its failure.</summary>
    /// <param name="verdict">The zone's verdict.</param>
    /// <returns>The error, code and kind together.</returns>
    public static Error From(DnssecTransitionVerdict verdict)
    {
        return verdict switch
        {
            DnssecTransitionVerdict.NotOff => Error.Of(nameof(ErrorMessages.ZoneSigningNotOff), ErrorType.Conflict),
            DnssecTransitionVerdict.NotSigning =>
                Error.Of(nameof(ErrorMessages.ZoneSigningNotSigning), ErrorType.Conflict),
            DnssecTransitionVerdict.NotWithdrawing =>
                Error.Of(nameof(ErrorMessages.ZoneSigningNotWithdrawing), ErrorType.Conflict),
            DnssecTransitionVerdict.TooEarly =>
                Error.Of(nameof(ErrorMessages.ZoneSigningWithdrawalTooEarly), ErrorType.Conflict),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "An accepted step has no error."),
        };
    }
}
