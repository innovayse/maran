namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>
/// Which of the three cases of <see cref="LicenceStatus"/> a status is, named without the data the
/// case carries, for the places that report the case alone: the wire's <c>State</c> field and the
/// localized label that travels beside it.
/// </summary>
/// <remarks>
/// <see cref="LicenceStatus"/> stays the type that carries a case's data; this enum exists because
/// the outward DTO cannot hold that hierarchy, and a literal string standing in for it would let a
/// fourth spelling reach the wire unnoticed. Like <see cref="LicenceRefusalReason"/> it is closed:
/// there is no member for "unknown", because the verifier produces exactly these three outcomes.
/// </remarks>
public enum LicenceStateKind
{
    /// <summary>A licence is installed and verified.</summary>
    Valid,

    /// <summary>No licence is installed: the ordinary first-run state, not a fault.</summary>
    Absent,

    /// <summary>A licence is installed and was not accepted; the reason names why.</summary>
    Refused,
}
