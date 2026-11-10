using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Models;

/// <summary>
/// What the Cron module hands to another server for one account: its scheduled entries and the environment
/// variables they run with (issue #127).
/// </summary>
/// <remarks>
/// Command text and environment values are the customer's own data, like the files in their home, and may
/// hold their application's secrets (decision D8): they travel inside a package that is plaintext at rest, and
/// the report says so. Nothing of the panel's own is in this record.
/// </remarks>
/// <param name="Entries">The account's cron entries.</param>
/// <param name="Environment">The environment variables the entries run with.</param>
public sealed record CronMigrationSection(
    IReadOnlyList<CronEntryMigrationItem> Entries,
    IReadOnlyList<CronEnvironmentVariableDto> Environment);
