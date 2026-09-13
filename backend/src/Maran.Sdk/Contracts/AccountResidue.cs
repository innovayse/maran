namespace Maran.Sdk.Contracts;

/// <summary>
/// What the post-cascade audit of a deleted account saw, and what it could not see: the rows that
/// still name the account, and the modules whose rows it failed to read at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two lists, because "clean" and "unchecked" are different answers and one of them used to be
/// reported as the other.</b> An audit that skipped a module has not found that module clean, and a
/// deletion that then reported COMPLETED would be making the same claim the defect made: a statement
/// about work nobody observed. Keeping the skipped modules in the answer is what lets the caller
/// say, in the operator's own task log, which modules its claim does and does not cover
/// (rules/testing.md — "state a check's blind spot in the check's own output").
/// </para>
/// <para>
/// Both lists are operator-facing diagnostic names, never customer-facing text and never localized:
/// they are entity and context type names, which is what an operator needs to find the module that
/// kept something.
/// </para>
/// </remarks>
/// <param name="Rows">
/// One entry per entity that still holds rows for the account, as <c>Entity(count)</c>. Empty means
/// nothing the audit could read still names the account.
/// </param>
/// <param name="Unchecked">
/// One entry per module context the audit could not read — an unmigrated schema, a context this
/// scope cannot build. Empty means every composed module was actually asked.
/// </param>
public sealed record AccountResidue(IReadOnlyList<string> Rows, IReadOnlyList<string> Unchecked)
{
    /// <summary>The answer of an audit that read every module and found nothing left.</summary>
    public static AccountResidue Clean { get; } = new([], []);
}
