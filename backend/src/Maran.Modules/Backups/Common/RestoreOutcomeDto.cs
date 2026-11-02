namespace Maran.Modules.Backups.Common;

/// <summary>Outward view of a restore that replaced the whole account.</summary>
/// <remarks>
/// <para>
/// <b>It exists only for a restore that succeeded, so it states nothing a success makes constant.</b>
/// A restore that left anything behind is a failure response with a code, never this body; on the
/// one path this DTO travels the account's home is the archive's home and every database in scope
/// was replaced. A verdict field would always read "whole", a files field would always read
/// "restored" and a total would always equal the restored count — constants wearing the clothes of
/// a measurement. The figure that does vary, how many databases were replaced, is the one field
/// the screen needs.
/// </para>
/// <para>
/// <b>There is no list of rolled-back databases here, and its absence is deliberate.</b> Those lists
/// live only on the agent's failing arms, inside diagnostic text that the agent client logs at its
/// own boundary and never carries outward — it can quote a database name and a path. The wire's
/// <c>not_rolled_back</c> field on the success message is documented as always empty, so a field
/// here would be a constant too. The partial-restore case is told by <see cref="RestorePartialDto"/>
/// on the failure response, and by the server's own log, which names the databases.
/// </para>
/// </remarks>
/// <param name="BackupId">The backup that was restored from.</param>
/// <param name="AccountId">The account that was replaced.</param>
/// <param name="DatabasesRestored">How many databases were dropped, re-created and loaded.</param>
public sealed record RestoreOutcomeDto(
    Guid BackupId,
    Guid AccountId,
    uint DatabasesRestored);
