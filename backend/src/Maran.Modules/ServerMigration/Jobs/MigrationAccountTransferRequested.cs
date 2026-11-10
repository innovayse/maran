using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>
/// The message that runs one pass of one account of a migration job (issue #197, phase 2). It carries ids and the
/// pass, never a key or a grant: the worker reads those from the rows when it needs them.
/// </summary>
/// <param name="AccountId">The job's row for the account.</param>
/// <param name="Kind">Which pass.</param>
public sealed record MigrationAccountTransferRequested(Guid AccountId, MigrationRunKind Kind);
