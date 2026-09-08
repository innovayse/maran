namespace Maran.Modules.Backups.Queries.ListBackupDestinations;

/// <summary>Lists every place this server records backups as living (spec §11).</summary>
/// <remarks>
/// It takes no filter and no paging. There is one destination on this build and there is no shape of
/// server on which there are many — a page parameter would be a promise about a list that cannot
/// grow, and a filter would be a query nobody can narrow.
/// </remarks>
public sealed record ListBackupDestinationsQuery;
