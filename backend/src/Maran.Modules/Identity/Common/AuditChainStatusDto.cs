namespace Maran.Modules.Identity.Common;

/// <summary>How far the audit journal has been sealed into the tamper-evidence chain.</summary>
/// <param name="SealedRows">Entries that have a place in the chain.</param>
/// <param name="UnsealedRows">Entries written since the last sealing run; they are not yet protected.</param>
/// <param name="LastChainIndex">The position of the newest sealed entry; <c>null</c> when none is sealed.</param>
/// <param name="LastSealedAt">
/// When the newest sealed entry HAPPENED (its own timestamp). Entries do not record when they were
/// sealed, so this is not the instant of the sealing run; it is how recent the protected part reaches.
/// </param>
/// <param name="PurgedThroughIndex">The last position retention has removed; zero when nothing was purged.</param>
public sealed record AuditChainStatusDto(
    long SealedRows,
    long UnsealedRows,
    long? LastChainIndex,
    DateTimeOffset? LastSealedAt,
    long PurgedThroughIndex);
