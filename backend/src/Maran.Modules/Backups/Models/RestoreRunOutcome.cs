namespace Maran.Modules.Backups.Models;

/// <summary>
/// What one restore run did, in the panel's own terms: the single answer the response, the panel
/// task and the audit entry are all written from.
/// </summary>
/// <remarks>
/// <para>
/// A carrier between this module's own layers (rules/csharp.md "Models/"). It mirrors no entity —
/// a restore writes no row of its own — and it states no rule.
/// </para>
/// <para>
/// <b>There is a <see cref="Whole"/> field and it is computed once, at the one place the agent's
/// outcome is read.</b> The alternative, letting each of the three writers compare
/// <see cref="DatabasesRestored"/> against <see cref="DatabasesTotal"/> for itself, is three
/// readings of one fact that can disagree — and the disagreement that matters is the one that
/// records a completed task over a restore that lost a database. The agent's own
/// <c>RestoreOutcome</c> deliberately has no such field so that the panel is forced to LOOK; this
/// type is where the panel finished looking, and everything downstream reads the answer rather than
/// re-deriving it.
/// </para>
/// <para>
/// <b>The failure is a whole <see cref="Error"/> and not a bare code, so that its KIND cannot be
/// re-invented downstream.</b> The handler used to build <c>Error.Of(code, ErrorType.Failure)</c>
/// from a loose string, which answered HTTP 500 over an artifact the agent had refused as unusable —
/// an <see cref="ErrorType.Validation"/> the agent client had already decided and this carrier threw
/// away. Splitting the two apart again would lose that decision, which is why they are one field
/// (rules/csharp.md "A type that exists to make two facts inseparable is stating a rule").
/// </para>
/// <para>
/// <b>What it does not carry: the rolled-back and not-rolled-back lists.</b> They exist only on the
/// agent's failing arms, as part of the diagnostic sentence the agent client logs and stops. The
/// wire's <c>not_rolled_back</c> field on the SUCCESS message is documented as always empty and the
/// agent client already refuses to carry it, so there is nothing here for it to be copied into. A
/// field that is a constant dressed as a measurement is the defect this whole plan is shaped
/// against.
/// </para>
/// </remarks>
/// <param name="Whole">
/// Whether the account was fully replaced: the files were restored AND every database the restore
/// set out to replace was replaced. The only thing a caller may read as success.
/// </param>
/// <param name="FilesRestored">Whether the account's home is now the archive's home.</param>
/// <param name="DatabasesRestored">How many databases were dropped, re-created and loaded.</param>
/// <param name="DatabasesTotal">How many the restore set out to replace, after the allowed list.</param>
/// <param name="Measured">
/// Whether the three figures above are the AGENT'S OWN statement — a terminal outcome it reported —
/// rather than the zeros of a run that stated nothing (a refusal, a dropped stream, an outcome-less
/// terminal event). The handler publishes a partial restore's counts to the caller only when this
/// is <c>true</c>: zeros that mean "unknown" must never reach an operator dressed as "nothing was
/// replaced".
/// </param>
/// <param name="Failure">
/// What went wrong — the machine-stable code AND its kind — or <c>null</c> when
/// <paramref name="Whole"/>. A PARTIAL restore carries one too: it is not a success, and a caller
/// that saw only the counts would have to invent a way to say so.
/// </param>
public sealed record RestoreRunOutcome(
    bool Whole,
    bool FilesRestored,
    uint DatabasesRestored,
    uint DatabasesTotal,
    bool Measured,
    Error? Failure);
