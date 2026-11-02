namespace Maran.Modules.Cron.Common;

/// <summary>The program ended on its own and returned a status (<c>kind</c> is <c>exited</c>).</summary>
/// <param name="ExitCode">The program's exit status; 0 is the status of a successful run.</param>
public sealed record CronRunExitedDto(int ExitCode) : CronRunEndDto;
