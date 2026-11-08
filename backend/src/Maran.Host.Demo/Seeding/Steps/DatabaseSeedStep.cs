using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Databases.Commands.CreateDatabase;
using Maran.Modules.Databases.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>Creates every account's databases and their users. The generated passwords are discarded.</summary>
public sealed class DatabaseSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public DatabaseSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "databases"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var account in context.World.Accounts)
        {
            foreach (var database in account.Databases)
            {
                await _runner.RunAsync<CreatedDatabaseDto>(
                    Name,
                    new CreateDatabaseCommand(
                        context.AccountId(account.Name),
                        database.Suffix,
                        database.UserSuffix,
                        DemoSeedCommandRunner.Origin,
                        DemoSeedCommandRunner.Agent),
                    cancellationToken);
            }
        }
    }
}
