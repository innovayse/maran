namespace Maran.Sdk.Contracts;

/// <summary>
/// The facts one module needs about a reseller owned by another: who it is and whether it may act.
/// Deliberately the smallest set that answers "what do I call this reseller, and may it start a
/// session" — not a copy of the reseller, and in particular not its envelope.
/// </summary>
/// <remarks>
/// A snapshot is read on the request path and used immediately; it is never stored, for the reason
/// <see cref="AccountSnapshot"/> gives. Identity reads it because it may not read the Accounts
/// schema: the banner of a view-as session names the reseller, and a login whose reseller is
/// suspended must not start a view-as.
/// </remarks>
/// <param name="Id">The reseller's identity, the same value a reseller login carries as its reseller id.</param>
/// <param name="Name">
/// The reseller's name, which is also the name of the login created for it. It is what an operator
/// recognises the reseller as, and what a banner and an audit subject say.
/// </param>
/// <param name="State">
/// Whether the reseller is in service or suspended. Required, with no default: a module deciding
/// whether to let anybody in must never read "active" from a snapshot that simply did not say.
/// </param>
public sealed record ResellerSnapshot(Guid Id, string Name, ResellerState State);
