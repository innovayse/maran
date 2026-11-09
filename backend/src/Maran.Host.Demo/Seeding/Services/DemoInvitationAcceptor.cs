using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Modules.Identity.Commands.AcceptInvitation;
using Maran.Modules.Identity.Common;
using Maran.SharedKernel.Security;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Turns an invitation the panel issued into an active login with a chosen password, by the same
/// accept command a person follows from their mail. Used because the demonstration publishes the
/// customer's password and the panel offers no other way to give a login one.
/// </summary>
public sealed class DemoInvitationAcceptor
{
    /// <summary>The query parameter that carries the token in an invitation link.</summary>
    private const string TokenParameter = "token=";

    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the acceptor.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public DemoInvitationAcceptor(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <summary>Accepts the invitation a link carries.</summary>
    /// <param name="step">The step, named in a failure.</param>
    /// <param name="link">The invitation link, absolute or relative.</param>
    /// <param name="password">The password the login gets.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>The login name the invitation was for.</returns>
    /// <exception cref="DemoSeedException">The link carries no token, or the panel refused it.</exception>
    public async Task<string> AcceptAsync(
        string step,
        SensitiveString link,
        string password,
        CancellationToken cancellationToken)
    {
        var text = link.Reveal();
        var start = text.IndexOf(TokenParameter, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new DemoSeedException(step, "InvitationLinkWithoutToken");
        }

        var token = Uri.UnescapeDataString(text[(start + TokenParameter.Length)..].Split('&')[0]);

        var accepted = await _runner.RunAsync<AcceptInvitationResultDto>(
            step,
            new AcceptInvitationCommand(
                token, password, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
            cancellationToken);

        return accepted.Username;
    }
}
