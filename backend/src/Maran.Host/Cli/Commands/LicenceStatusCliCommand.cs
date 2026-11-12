using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Modules.Licensing.Services;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// <c>licence-status</c>: prints one line saying what licence is installed and where its lease stands, and a
/// second line with this server's id. Read-only; <c>maran status</c> shows both under "Panel". Composition and
/// dispatch only: what the lines say is decided by the Licensing module (<see cref="LicenceStatusLine"/>).
/// </summary>
/// <remarks>
/// A command-line operator can already read the licence file and <c>/etc/machine-id</c>, so the lines disclose
/// nothing new; the licence line carries no licence text or signature, and the server id is the value a licence
/// names to be bound here, which the marketplace asks for when a licence is claimed. The command never throws on
/// a licence problem: a missing, refused or expired licence is a line, not a failure, and only an unexpected
/// fault is a non-zero exit.
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
        var line = scope.ServiceProvider.GetRequiredService<LicenceStatusLine>();

        await console.Output.WriteLineAsync(await line.DescribeAsync(cancellationToken));

        var warning = await line.DescribeExpiryWarningAsync(cancellationToken);
        if (warning is not null)
        {
            await console.Output.WriteLineAsync(warning);
        }

        await console.Output.WriteLineAsync(await line.DescribeServerAsync(cancellationToken));

        return new CliResult(CliExitCode.Success);
    }
}
