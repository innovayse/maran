using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Backups.Commands.CreateBackup;
using Maran.Modules.Backups.Commands.SaveBackupSchedule;
using Maran.Modules.Backups.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>Takes each account's backups so its history is populated, and saves its schedule.</summary>
public sealed class BackupSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public BackupSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "backups"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var account in context.World.Accounts)
        {
            var accountId = context.AccountId(account.Name);

            for (var taken = 0; taken < account.BackupCount; taken++)
            {
                await _runner.RunAsync<BackupDto>(
                    Name,
                    new CreateBackupCommand(accountId, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                    cancellationToken);
            }

            if (account.BackupSchedule is { } schedule)
            {
                await _runner.RunAsync<BackupScheduleDto>(
                    Name,
                    new SaveBackupScheduleCommand(
                        accountId,
                        null,
                        schedule.Frequency,
                        schedule.HourUtc,
                        schedule.DayOfWeekUtc,
                        schedule.RetainCount,
                        true,
                        DemoSeedCommandRunner.Origin,
                        DemoSeedCommandRunner.Agent),
                    cancellationToken);
            }
        }
    }
}
