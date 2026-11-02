namespace Maran.Modules.Cron.Domain.ValueObjects;

/// <summary>The program ended on its own and returned a status.</summary>
/// <param name="ExitCode">The program's exit status; 0 is the status of a successful run.</param>
public sealed record CronRunExited(int ExitCode) : CronRunEnd;
