using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Identity.Commands.IssueInvitationLink;
using Maran.Modules.Identity.Common;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Creates every hosting account, and gives the featured customer's login the published password.
/// The other owners keep a pending invitation: the demonstration publishes one customer sign-in only.
/// </summary>
public sealed class AccountSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Activates a login from its invitation.</summary>
    private readonly DemoInvitationAcceptor _acceptor;

    /// <summary>The published sign-ins.</summary>
    private readonly DemoIdentityOptions _identities;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    /// <param name="acceptor">Activates a login from its invitation.</param>
    /// <param name="identities">The published sign-ins.</param>
    public AccountSeedStep(
        DemoSeedCommandRunner runner,
        DemoInvitationAcceptor acceptor,
        IOptions<DemoIdentityOptions> identities)
    {
        _runner = runner;
        _acceptor = acceptor;
        _identities = identities.Value;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "accounts"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var account in context.World.Accounts)
        {
            Guid? resellerId = account.ResellerName is { } reseller ? context.ResellerIds[reseller] : null;

            var created = await _runner.RunAsync<AccountDto>(
                Name,
                new CreateAccountCommand(
                    account.Name,
                    account.PrimaryDomain,
                    context.PlanFor(account),
                    account.OwnerEmail,
                    account.OwnerLanguage,
                    resellerId,
                    DemoSeedCommandRunner.Origin,
                    DemoSeedCommandRunner.Agent),
                cancellationToken);
            context.AccountIds[account.Name] = created.Id;
        }

        var featured = context.AccountId(context.World.FeaturedCustomerAccountName);
        var link = await _runner.RunAsync<InvitationLinkDto>(
            Name,
            new IssueInvitationLinkCommand(featured, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
            cancellationToken);
        await _acceptor.AcceptAsync(Name, link.Link, _identities.CustomerPassword, cancellationToken);
    }
}
