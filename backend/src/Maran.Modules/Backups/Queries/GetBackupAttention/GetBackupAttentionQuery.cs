namespace Maran.Modules.Backups.Queries.GetBackupAttention;

/// <summary>
/// Asks which accounts' most recent finished backup failed, optionally for one account only. Bound
/// from the query string (<c>GET /api/v1/backups/attention?accountId=…</c>).
/// </summary>
/// <remarks>
/// A question of its own rather than something a screen derives from the history, because the
/// history is paged: a page holds some rows, and "the latest finished run of every account" is a
/// statement about all of them. The account narrows the caller's tenant scope and never replaces it.
/// </remarks>
/// <param name="AccountId">Only this account, or <c>null</c> for every account the caller may see.</param>
public sealed record GetBackupAttentionQuery(Guid? AccountId = null);
