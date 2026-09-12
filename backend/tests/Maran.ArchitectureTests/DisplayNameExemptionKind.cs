namespace Maran.ArchitectureTests;

/// <summary>The three ways a vocabulary member may be excused from carrying a display name.</summary>
/// <remarks>
/// There are exactly three because each one is verifiable by a different piece of evidence, and an
/// exemption whose evidence cannot be checked is a comment, not an exemption. A fourth kind meaning
/// "we discussed it and it is fine" is the shape this enum exists to refuse.
/// </remarks>
public enum DisplayNameExemptionKind
{
    /// <summary>
    /// The SPA owns the wording, because the member's set is closed and the bundle can hold a word
    /// for every value in it. Evidence is a locale key template; the law resolves it for every
    /// member of the enum in English, Russian and Armenian, so the claim dies the day a key is
    /// missing or a member is added without one. Never available to a <c>string</c> member: an
    /// open vocabulary cannot be enumerated by a bundle that shipped before its values existed.
    /// </summary>
    SpaOwnedVocabulary,

    /// <summary>
    /// The value is not shown to an operator as a name at all — it is printed as a machine
    /// identifier and framed as one, it is the SPA's own control value echoed back, it is named by
    /// the platform's locale data, or no screen reads it. Evidence is the SPA file that makes that
    /// true; the law checks the file exists and still mentions the member, so a rename or a new
    /// screen invalidates the excuse instead of quietly outliving it.
    /// </summary>
    NotAnOperatorFacingName,

    /// <summary>
    /// A defect the law found and nobody has fixed yet. This is debt, recorded so the law can be
    /// green for everything else, and it is one-way: the law asserts every pending entry STILL
    /// violates, so a fixed member forces its entry to be deleted rather than letting the list rot
    /// into a list of things that used to be wrong.
    /// </summary>
    PendingFix,
}
