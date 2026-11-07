namespace Maran.Modules.Identity.Common;

/// <summary>The result of verifying the audit chain, as shown to the administrator who asked.</summary>
/// <param name="Intact"><c>true</c> when every sealed entry checked out.</param>
/// <param name="RowsChecked">How many sealed entries were examined.</param>
/// <param name="FirstBrokenChainIndex">The position at which the walk stopped; <c>null</c> when intact.</param>
/// <param name="Break">The name of what was wrong (<c>None</c> when intact).</param>
/// <param name="VerifiedAt">When the verification finished.</param>
public sealed record AuditChainVerificationDto(
    bool Intact,
    long RowsChecked,
    long? FirstBrokenChainIndex,
    string Break,
    DateTimeOffset VerifiedAt);
