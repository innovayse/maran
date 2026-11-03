namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The complete list of controller actions an operator viewing as a customer login may use to change
/// something (issue #334).
/// </summary>
/// <remarks>
/// <para>
/// A safe mutation is the one declaration that WIDENS what a view-as token can do, so it is a
/// decision with a written reason, reviewed in a diff of this file rather than found by grep. A
/// <see cref="Maran.Sdk.Contracts.ViewAsAccess.SafeMutation"/> that is not listed here fails
/// <see cref="ViewAsAccessDeclarationTests"/>; an entry whose action no longer exists or no longer
/// declares one fails it too. The threat note
/// (docs/superpowers/notes/2026-11-03-view-as-allow-list-threat-note.md) states the test an entry
/// must pass.
/// </para>
/// </remarks>
public static class ViewAsSafeMutationPin
{
    /// <summary>Every safe-mutation controller action of the composed panel, with its reason.</summary>
    public static IReadOnlyList<PinnedSafeMutation> All { get; } = new List<PinnedSafeMutation>
    {
        new("PinnedPagesController.PinAsync", "A bookmark in the viewed login's menu: grants no access and runs nothing."),
        new("PinnedPagesController.UnpinAsync", "Removes a bookmark from the viewed login's menu: grants no access and runs nothing."),
        new("ViewAsController.EndAsync", "Ends the operator's own view-as session; it is the way out and changes no customer data."),
    };
}
