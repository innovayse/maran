using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Firewall.Commands.AddWhitelistEntry;
using Maran.Modules.Firewall.Commands.AllowPort;
using Maran.Modules.Firewall.Commands.BanAddress;
using Maran.Modules.Firewall.Commands.DenyPort;
using Maran.Modules.Firewall.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Adds the world's port rules, bans and whitelist entries. The simulated server keeps the rule state
/// and enforces nothing, so no address is ever really blocked.
/// </summary>
public sealed class FirewallSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public FirewallSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "firewall"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var firewall = context.World.Firewall;

        foreach (var rule in firewall.PortRules)
        {
            object command = rule.Allow
                ? new AllowPortCommand(
                    rule.Port,
                    rule.Protocol,
                    rule.SourceCidr,
                    null,
                    DemoSeedCommandRunner.Origin,
                    DemoSeedCommandRunner.Agent)
                : new DenyPortCommand(
                    rule.Port,
                    rule.Protocol,
                    rule.SourceCidr,
                    null,
                    DemoSeedCommandRunner.Origin,
                    DemoSeedCommandRunner.Agent);
            await _runner.RunAsync(Name, command, cancellationToken);
        }

        foreach (var ban in firewall.Bans)
        {
            await _runner.RunAsync(
                Name,
                new BanAddressCommand(
                    ban.Address, ban.DurationMinutes, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
        }

        foreach (var entry in firewall.Whitelist)
        {
            await _runner.RunAsync<WhitelistEntryDto>(
                Name,
                new AddWhitelistEntryCommand(
                    entry.Cidr, entry.Note, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
        }
    }
}
