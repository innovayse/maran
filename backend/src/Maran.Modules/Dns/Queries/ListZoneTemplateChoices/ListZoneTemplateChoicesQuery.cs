namespace Maran.Modules.Dns.Queries.ListZoneTemplateChoices;

/// <summary>
/// Reads the zone templates a caller who may create a zone can pick from: their id, name and whether each is the
/// default, never their records.
/// </summary>
public sealed record ListZoneTemplateChoicesQuery;
