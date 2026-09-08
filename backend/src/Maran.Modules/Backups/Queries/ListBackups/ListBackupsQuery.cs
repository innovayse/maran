namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// Lists the backups the caller may see: their own account's, or every account's for an
/// administrator.
/// </summary>
/// <remarks>
/// Parameterless on purpose. There is no account parameter because there is nothing for one to do:
/// the context's global query filter already scopes the read to the caller's tenant, so a customer
/// supplying an account id could only ever ask for rows they were going to be shown anyway — or for
/// rows they were not, which is the question this type declines to make askable.
/// </remarks>
public sealed record ListBackupsQuery();
