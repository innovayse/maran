namespace Maran.Modules.Accounts.Queries.GetReseller;

/// <summary>Reads one reseller, with its envelope and what it owns.</summary>
/// <param name="ResellerId">The reseller to read.</param>
public sealed record GetResellerQuery(Guid ResellerId);
