namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "may this zone move to the next step of its DNSSEC life", and when not, why.</summary>
public enum DnssecTransitionVerdict
{
    /// <summary>The zone moved and its serial moved with it.</summary>
    Accepted,

    /// <summary>Signing was asked for on a zone that is already signed or being withdrawn.</summary>
    NotOff,

    /// <summary>A withdrawal was asked for on a zone that is not signed.</summary>
    NotSigning,

    /// <summary>The end of a withdrawal was asked for on a zone that is not being withdrawn.</summary>
    NotWithdrawing,

    /// <summary>The end of a withdrawal was asked for before the minimum wait since it began has passed.</summary>
    TooEarly,
}
