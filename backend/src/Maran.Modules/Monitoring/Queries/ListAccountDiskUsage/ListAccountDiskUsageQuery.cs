namespace Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;

/// <summary>Reads how much disk every hosting account occupies, beside what its plan allows.</summary>
/// <remarks>
/// Takes no parameters, and that is the whole surface: there is no account id to pass and therefore
/// nothing a caller can probe with. The listing is host-wide by nature — its question is "which
/// account is filling this server" — so narrowing it to one account would answer a question the
/// dashboard does not ask while adding the one input this query would have to defend.
/// </remarks>
public sealed record ListAccountDiskUsageQuery;
