using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Cron.Commands.CreateCronEntry;
using Maran.Modules.Cron.Commands.RunCronEntry;
using Maran.Modules.Cron.Commands.SetCronEntryEnabled;
using Maran.Modules.Cron.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Creates every account's scheduled jobs, switches off the ones the world leaves off, and runs the
/// ones marked so once, so their history holds a run. The simulated server stores the command line
/// and never interprets it.
/// </summary>
public sealed class CronSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public CronSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "cron"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var account in context.World.Accounts)
        {
            var accountId = context.AccountId(account.Name);

            foreach (var entry in account.CronEntries)
            {
                var created = await _runner.RunAsync<CronEntryDto>(
                    Name,
                    new CreateCronEntryCommand(
                        accountId,
                        entry.Schedule,
                        entry.Command,
                        DemoSeedCommandRunner.Origin,
                        DemoSeedCommandRunner.Agent),
                    cancellationToken);

                if (entry.RunOnce)
                {
                    await _runner.RunAsync<CronRunDto>(
                        Name,
                        new RunCronEntryCommand(
                            accountId, created.EntryId, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                        cancellationToken);
                }

                if (!entry.Enabled)
                {
                    await _runner.RunAsync(
                        Name,
                        new SetCronEntryEnabledCommand(
                            accountId,
                            false,
                            created.EntryId,
                            DemoSeedCommandRunner.Origin,
                            DemoSeedCommandRunner.Agent),
                        cancellationToken);
                }
            }
        }
    }
}
