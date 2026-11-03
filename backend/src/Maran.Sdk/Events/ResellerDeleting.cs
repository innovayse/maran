namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a reseller is removed, so that the Identity
/// module can take away the login the reseller signs in with, and every session of it.
/// </summary>
/// <remarks>
/// Present tense, and it is load-bearing, for the reason <see cref="AccountDeleting"/> gives: it is
/// invoked inline before anything has been removed, and a subscriber that throws ABORTS the deletion.
/// A reseller still owning accounts is refused by the Accounts module before this is announced, so a
/// subscriber never has to decide what becomes of a reseller's customers.
/// </remarks>
/// <param name="ResellerId">
/// The reseller about to be removed. The subscriber deletes the login carrying this reseller id and
/// touches nothing else — this is a cascade, not an invitation to reach across a schema.
/// </param>
public sealed record ResellerDeleting(Guid ResellerId);
