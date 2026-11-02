namespace Maran.Modules.Cron.Queries.GetCronRun;

/// <summary>One run started by hand, with what it printed (issue #158).</summary>
/// <param name="AccountId">The account whose entry it is; another tenant's answers "not found".</param>
/// <param name="EntryId">The entry the run belongs to.</param>
/// <param name="RunId">The run's identity.</param>
public sealed record GetCronRunQuery(Guid AccountId, string EntryId, Guid RunId);
