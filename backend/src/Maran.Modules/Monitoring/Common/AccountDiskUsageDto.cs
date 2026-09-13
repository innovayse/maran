namespace Maran.Modules.Monitoring.Common;

/// <summary>What one hosting account occupies on disk, beside what its plan allows.</summary>
/// <remarks>
/// <para>
/// <b>Two sources, joined here and nowhere else.</b> The used figure is the agent's — it is the only
/// thing in the system that can walk a home directory — and the allowance is the panel's, stored
/// against the account's plan. Neither side holds both: the agent deliberately produces no quota
/// (the wire's <c>quota_bytes</c> is written 0 and is not projected at all), and the panel cannot
/// measure a filesystem it does not run on.
/// </para>
/// <para>
/// <b>Both figures are in BYTES, and the conversion happens on the way in.</b> A plan stores its
/// allowance in megabytes, which is what an operator typed; putting a megabyte figure beside a byte
/// figure in one row is how a reader ends up drawing a bar a million times too short. One unit per
/// type, chosen as the finer of the two so nothing rounds.
/// </para>
/// </remarks>
/// <param name="AccountId">The account's identity, the same value a tenant-scoped row carries.</param>
/// <param name="Username">The account's Linux system user name — what the agent measured under.</param>
/// <param name="UsedBytes">
/// Bytes occupied under the account's home directory, or <c>null</c> when the agent did not report
/// this account at all. Nullable and never 0 for that case, deliberately: a zero is a claim that the
/// account holds nothing, and an account the agent could not see is one nobody has measured. The
/// interface must draw the difference — "empty" and "unknown" are not the same row.
/// </param>
/// <param name="QuotaBytes">
/// The plan's allowance in bytes, converted from the megabytes it is stored in. Reported as it
/// stands, including a zero: what a zero-quota plan means is the Accounts module's question, and
/// inventing an answer here would put a second opinion about a plan outside the module that owns it.
/// </param>
public sealed record AccountDiskUsageDto(Guid AccountId, string Username, long? UsedBytes, long QuotaBytes);
