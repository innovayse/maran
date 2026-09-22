using Maran.Modules.Licensing.Domain.Entities;

namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>
/// The three-state answer <see cref="Services.LicenceVerifier"/> gives about the currently
/// installed licence. Never a <c>bool</c>, and never two states dressed as three: <c>Absent</c> and
/// <c>Refused</c> are siblings, not one collapsed into the other.
/// </summary>
/// <remarks>
/// <para>
/// This is the single most load-bearing type in this slice. An operator with no licence and an
/// operator with a corrupted or forged one need two different remedies — "install one" versus
/// "get a fresh copy, do not hand-edit it" — and a result shaped as a bare pass/fail (or as
/// <c>Refused</c> with <c>Absent</c> folded in as one more reason) cannot tell them apart. The same
/// argument the code-integrity lane already made for its own three-state result
/// (<c>CodeIntegrityOutcome</c>: <c>Clean</c>/<c>Drifted</c>/<c>Unavailable</c>, never a bool)
/// applies here for the identical reason: collapsing "nothing to check" into "checked and it
/// failed" is the shape that looks healthiest while telling the operator the least.
/// </para>
/// <para>
/// <b>Kept a closed hierarchy of records, not a class with a nullable reason.</b> A caller pattern
/// -matches on the concrete case; there is no state where <see cref="Refused"/>'s
/// <see cref="LicenceRefusalReason"/> is absent, and no state where <see cref="Valid"/> carries no
/// licence — both would be representable by a looser shape and are not representable by this one.
/// </para>
/// </remarks>
public abstract record LicenceStatus
{
    /// <summary>Private constructor: only the three cases below may derive from this type.</summary>
    private LicenceStatus()
    {
    }

    /// <summary>A licence is installed and verified: signature, product and expiry all checked out.</summary>
    /// <param name="Licence">The accepted licence, carrying tier, modules and expiry for the caller to use.</param>
    public sealed record Valid(Licence Licence) : LicenceStatus;

    /// <summary>Nothing is installed. The ordinary first-run state; not a failure of anything.</summary>
    public sealed record Absent : LicenceStatus
    {
        /// <summary>The single shared instance; an absent licence carries no state of its own.</summary>
        public static readonly Absent Instance = new();
    }

    /// <summary>Something WAS installed and it did not verify.</summary>
    /// <param name="Reason">The specific, actionable reason it was refused.</param>
    public sealed record Refused(LicenceRefusalReason Reason) : LicenceStatus;
}
