namespace Maran.Modules.Backups.Queries.CountFailedBackups;

/// <summary>
/// Counts the failed backups the caller may see, for a screen that shows how many need attention
/// rather than which. A count is asked as a count: the history list is paged, so a page of it holds
/// one page and not the total.
/// </summary>
public sealed record CountFailedBackupsQuery;
