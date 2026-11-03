namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a reseller is marked suspended, so that the
/// Identity module can suspend the login the reseller signs in with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Present tense, and it is load-bearing.</b> Like <see cref="AccountSuspending"/> it is invoked
/// inline before the row is written, and a subscriber that throws ABORTS the suspension and leaves
/// the reseller as it was — the recoverable direction, since a reseller still active can be
/// suspended again, whereas one marked suspended whose login still signs in is a lie to billing.
/// </para>
/// <para>
/// <b>What it covers, and what it does not.</b> It covers the reseller's LOGIN and nothing else. It
/// does NOT suspend any account the reseller owns: those are the customers' services and keep
/// running, and an administrator suspends them separately if that is wanted. A subscriber must not
/// treat this event as a cascade over the reseller's accounts.
/// </para>
/// </remarks>
/// <param name="ResellerId">
/// The reseller about to be suspended. The subscriber acts on the login carrying this reseller id
/// and touches nothing else.
/// </param>
public sealed record ResellerSuspending(Guid ResellerId);
