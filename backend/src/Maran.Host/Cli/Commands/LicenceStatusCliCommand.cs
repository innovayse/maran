using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Modules.Licensing.Services;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// <c>licence-status</c>: prints one line saying what licence is installed and where its lease stands.
/// Read-only; <c>maran status</c> shows the line under "Panel". Composition and dispatch only: what the
/// line says is decided by the Licensing module (<see cref="LicenceStatusLine"/>).
/// </summary>
/// <remarks>
/// A command-line operator can already read the licence file, so the line discloses nothing new; it carries
/// no licence text, signature or machine id. The command never throws on a licence problem: a missing,
/// refused or expired licence is a line, not a failure, and only an unexpected fault is a non-zero exit.
/// </remarks>
public sealed class LicenceStatusCliCommand : ICliCommand
{
    /// <summary>The verb the command line dispatches on.</summary>
    public const string VerbName = "licence-status";

    /// <summary>Opens the scope the line's scoped dependencies resolve from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Creates the command.</summary>
    /// <param name="scopeFactory">Opens the scope the line's scoped dependencies resolve from.</param>
    public LicenceStatusCliCommand(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return VerbName; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        if (invocation.Arguments.Count != 0)
        {
            await console.Error.WriteLineAsync("usage: licence-status");
            return new CliResult(CliExitCode.Usage);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var line = await scope.ServiceProvider
            .GetRequiredService<LicenceStatusLine>()
            .DescribeAsync(cancellationToken);

        await console.Output.WriteLineAsync(line);

        return new CliResult(CliExitCode.Success);
    }
}
