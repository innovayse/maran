namespace Maran.ArchitectureTests;

/// <summary>One declared excuse for a vocabulary member that carries no localized display name.</summary>
/// <remarks>
/// Every field is required, and <see cref="Evidence"/> is the point of the type: a reason on its own
/// is a sentence anybody can write, while evidence is a locale key template or a file path the law
/// goes and checks. The reason is still mandatory, because the evidence says WHAT is true and only
/// the reason says why that is acceptable.
/// </remarks>
/// <param name="DtoTypeName">Full name of the outward DTO, as the runtime reports it.</param>
/// <param name="MemberName">The member being excused.</param>
/// <param name="Kind">Which kind of excuse this is, and therefore how it is verified.</param>
/// <param name="Evidence">
/// For <see cref="DisplayNameExemptionKind.SpaOwnedVocabulary"/>, a locale key template containing
/// <c>{member}</c>, resolved per enum member in every language. For
/// <see cref="DisplayNameExemptionKind.NotAnOperatorFacingName"/>, a repository-relative path to the
/// SPA file that makes the claim true. For <see cref="DisplayNameExemptionKind.PendingFix"/>, where
/// the defect shows on screen.
/// </param>
/// <param name="Reason">Why this member is allowed to reach the wire without a name beside it.</param>
public sealed record DisplayNameExemption(
    string DtoTypeName,
    string MemberName,
    DisplayNameExemptionKind Kind,
    string Evidence,
    string Reason);
