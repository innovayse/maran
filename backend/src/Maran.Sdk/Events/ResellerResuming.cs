namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a suspended reseller is marked active again,
/// so that the Identity module can let its login sign in once more.
/// </summary>
/// <remarks>
/// Present tense for the reason <see cref="ResellerSuspending"/> is: invoked inline before the row
/// is written, so a subscriber that throws aborts the resumption and the reseller stays suspended,
/// which is the recoverable direction. It restores only what <see cref="ResellerSuspending"/>
/// changed — the login — and no account is touched either way.
/// </remarks>
/// <param name="ResellerId">
/// The reseller about to be reactivated. The subscriber acts on the login carrying this reseller id.
/// </param>
public sealed record ResellerResuming(Guid ResellerId);
