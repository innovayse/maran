using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Extensions;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Modules.Ssl.Services;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Break-glass issuance of the panel's own certificate: orders a certificate for the configured host
/// name, installs it as the pair the interface is served with, and records it so the daily sweep
/// renews it. Run again, it renews. For the operator who cannot ask for it from the interface.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes no argument.</b> The host name is the operator's own configuration, read by the Ssl
/// module, never typed here: the verb cannot be aimed at another name, and there is no value on the
/// command line for the wrapper to validate or a shell to misread.
/// </para>
/// <para>
/// <b>Who is accountable.</b> The journal row claims the operator named by <c>SUDO_USER</c>, else
/// <c>root</c>. It is a claim and not an authentication: the proof that the caller is root is the
/// wrapper's check, not this process's.
/// </para>
/// <para>
/// Whether a name is configured, what the authority answers and what the agent installs are decided
/// by <see cref="PanelCertificateConsoleIssuer"/>, in the module that owns them; this verb only
/// carries the request in and the answer out. A failure is reported by its code on standard error,
/// as every verb does, because a failed order's text can carry the authority's own words.
/// </para>
/// </remarks>
public sealed class IssuePanelCertificateCliCommand : ICliCommand
{
    /// <summary>The operator recorded when sudo did not name one.</summary>
    private const string DefaultOperator = "root";

    /// <summary>Opens the scope the issuer is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per run, because the issuer holds a scoped context.</param>
    public IssuePanelCertificateCliCommand(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return "issue-panel-certificate"; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        if (invocation.Arguments.Count != 0)
        {
            await console.Error.WriteLineAsync($"usage: {Verb}");
            return new CliResult(CliExitCode.Usage);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var issuer = scope.ServiceProvider.GetRequiredService<PanelCertificateConsoleIssuer>();
        var result = await issuer.IssueAsync(invocation.SudoUser ?? DefaultOperator, cancellationToken);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            await console.Error.WriteLineAsync($"Not issued: {error.Code}.");
            return new CliResult(error.Type.ToCliExitCode());
        }

        var certificate = result.Value;
        await console.Output.WriteLineAsync(
            $"Certificate for {certificate.HostName} installed; it expires at {certificate.ExpiresAt:u}.");

        return new CliResult(CliExitCode.Success);
    }
}
