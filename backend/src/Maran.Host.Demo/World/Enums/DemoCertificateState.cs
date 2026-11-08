namespace Maran.Host.Demo.World.Enums;

/// <summary>The certificate state the demo world leaves a site in.</summary>
public enum DemoCertificateState
{
    /// <summary>The site carries no certificate.</summary>
    None = 0,

    /// <summary>A certificate issued a moment ago, far from expiry.</summary>
    Valid = 1,

    /// <summary>A certificate a few days from expiry whose renewal has not yet been attempted.</summary>
    ExpiringSoon = 2,

    /// <summary>A certificate near expiry whose last renewal attempt failed.</summary>
    RenewalFailed = 3,
}
