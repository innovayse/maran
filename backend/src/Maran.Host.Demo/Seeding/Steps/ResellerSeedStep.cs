using System.Security.Cryptography;
using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Accounts.Commands.CreateReseller;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Identity.Commands.IssueResellerInvitationLink;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Callers;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Creates each reseller and activates its login. The login is given a random password that is
/// written nowhere, so the reseller can be viewed as by the administrator but nobody can sign in as it.
/// </summary>
public sealed class ResellerSeedStep : IDemoSeedStep
{
    /// <summary>Bytes of randomness behind the discarded password.</summary>
    private const int PasswordBytes = 33;

    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Activates a login from its invitation.</summary>
    private readonly DemoInvitationAcceptor _acceptor;

    /// <summary>Opens the scope the identity context is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    /// <param name="acceptor">Activates a login from its invitation.</param>
    /// <param name="scopeFactory">Opens the scope the identity context is resolved in.</param>
    public ResellerSeedStep(
        DemoSeedCommandRunner runner,
        DemoInvitationAcceptor acceptor,
        IServiceScopeFactory scopeFactory)
    {
        _runner = runner;
        _acceptor = acceptor;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "resellers"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var definition in context.World.Resellers)
        {
            var reseller = await _runner.RunAsync<ResellerDetailDto>(
                Name,
                new CreateResellerCommand(
                    definition.Name,
                    definition.ContactEmail,
                    definition.Envelope,
                    "en",
                    DemoSeedCommandRunner.Origin,
                    DemoSeedCommandRunner.Agent),
                cancellationToken);
            context.ResellerIds[definition.Name] = reseller.Id;

            var link = await _runner.RunAsync<InvitationLinkDto>(
                Name,
                new IssueResellerInvitationLinkCommand(
                    reseller.Id, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
            var username = await _acceptor.AcceptAsync(
                Name,
                link.Link,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(PasswordBytes)),
                cancellationToken);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var userId = await identity.Users
                .AsNoTracking()
                .Where(user => user.Username == username)
                .Select(user => user.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (userId == Guid.Empty)
            {
                throw new DemoSeedException(Name, "ResellerLoginMissing");
            }

            context.ResellerUserIds[definition.Name] = userId;
        }
    }

    /// <summary>The caller that acts for a seeded reseller.</summary>
    /// <param name="context">What the steps share.</param>
    /// <param name="resellerName">The reseller's name.</param>
    /// <returns>The reseller caller.</returns>
    public static ResellerCaller CallerFor(DemoSeedContext context, string resellerName)
    {
        return new ResellerCaller(
            context.ResellerUserIds[resellerName],
            resellerName,
            context.ResellerIds[resellerName]);
    }
}
