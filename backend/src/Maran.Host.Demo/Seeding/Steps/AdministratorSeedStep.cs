using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World;
using Maran.Modules.Identity.Commands.CompleteSetup;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Options;
using Maran.SharedKernel.Callers;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Creates the published administrator through first-run setup, the only door that creates one. The
/// setup token must be configured; setup closes for good the moment this succeeds, because the panel
/// then holds a user.
/// </summary>
public sealed class AdministratorSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>The published sign-ins.</summary>
    private readonly DemoIdentityOptions _identities;

    /// <summary>The first-run token.</summary>
    private readonly SetupOptions _setup;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    /// <param name="identities">The published sign-ins.</param>
    /// <param name="setup">The first-run token.</param>
    public AdministratorSeedStep(
        DemoSeedCommandRunner runner,
        IOptions<DemoIdentityOptions> identities,
        IOptions<SetupOptions> setup)
    {
        _runner = runner;
        _identities = identities.Value;
        _setup = setup.Value;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "administrator"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_setup.Token))
        {
            throw new DemoSeedException(Name, "SetupTokenNotConfigured");
        }

        var user = await _runner.RunAsync<AuthenticatedUserDto>(
            Name,
            new CompleteSetupCommand(
                _setup.Token,
                _identities.AdministratorUsername,
                _identities.AdministratorEmail,
                _identities.AdministratorPassword,
                DemoSeedCommandRunner.Origin,
                DemoSeedCommandRunner.Agent),
            cancellationToken);

        context.Administrator = new AdministratorCaller(user.Id, user.Username);
    }
}
