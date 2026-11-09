using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World.Enums;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Moves accounts into the state the world leaves them in. It runs last, because a suspended account
/// accepts no new resources and every other step needs the account active.
/// </summary>
public sealed class AccountStateSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public AccountStateSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "account-states"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var account in context.World.Accounts.Where(account =>
        {
            return account.State == DemoAccountState.Suspended;
        }))
        {
            await _runner.RunAsync<AccountDto>(
                Name,
                new SuspendAccountCommand(
                    context.AccountId(account.Name), DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
        }
    }
}
