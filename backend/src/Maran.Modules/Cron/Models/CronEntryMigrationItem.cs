using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Models;

/// <summary>One cron entry of an account as it travels to another server.</summary>
/// <param name="Schedule">The five fields of the schedule.</param>
/// <param name="Command">The command line, which is the customer's own data and may name their secrets.</param>
/// <param name="Enabled">Whether the entry was enabled on the sending server.</param>
public sealed record CronEntryMigrationItem(CronScheduleDto Schedule, string Command, bool Enabled);
