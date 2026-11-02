namespace Maran.Modules.Backups.Models;

/// <summary>
/// What one restore run did, in the panel's own terms: the single answer the response, the panel
/// task and the audit entry are all written from. One of exactly three cases, each its own type, so
/// a figure exists only on the case that can state it.
/// </summary>
/// <remarks>
/// <para>
/// A carrier between this module's own layers (rules/csharp.md "Models/"). It mirrors no entity —
/// a restore writes no row of its own — and it states no rule beyond the one in
/// <see cref="Services.RestoreRunner"/>.
/// </para>
/// <para>
/// <b>The verdict is the TYPE, decided once, at the one place the agent's outcome is read.</b> The
/// alternative, letting each of the three writers compare the restored count against the total for
/// itself, is three readings of one fact that can disagree — and the disagreement that matters is
/// the one that records a completed task over a restore that lost a database. The agent's own
/// <c>RestoreOutcome</c> deliberately has no success field so that the panel is forced to LOOK; the
/// runner is where the panel finished looking, and everything downstream matches on the case rather
/// than re-deriving it. The cases are <see cref="WholeRestoreOutcome"/> (the account was fully
/// replaced), <see cref="PartialRestoreOutcome"/> (the agent stated what it replaced before
/// stopping) and <see cref="UnmeasuredRestoreOutcome"/> (the agent stated nothing usable).
/// </para>
/// <para>
/// <b>What it does not carry: the rolled-back and not-rolled-back lists.</b> They exist only on the
/// agent's failing arms, as part of the diagnostic sentence the agent client logs and stops. The
/// wire's <c>not_rolled_back</c> field on the SUCCESS message is documented as always empty and the
/// agent client already refuses to carry it, so there is nothing here for it to be copied into.
/// </para>
/// </remarks>
public abstract record RestoreRunOutcome
{
    /// <summary>Closes the hierarchy to the three cases this module declares.</summary>
    private protected RestoreRunOutcome()
    {
    }
}
