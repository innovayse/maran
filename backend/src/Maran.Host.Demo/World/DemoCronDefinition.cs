using Maran.Modules.Cron.Common;

namespace Maran.Host.Demo.World;

/// <summary>One scheduled job of the demo world.</summary>
/// <param name="Schedule">When the job runs.</param>
/// <param name="Command">The one command line the job runs; stored as data and never interpreted by the demo.</param>
/// <param name="Enabled">Whether the job is switched on.</param>
/// <param name="RunOnce">Whether the seed runs the job once so it has a run in its history.</param>
public sealed record DemoCronDefinition(CronScheduleDto Schedule, string Command, bool Enabled, bool RunOnce);
