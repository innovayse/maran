using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Demo.Reset;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World;

namespace Maran.Host.Demo.Cli;

/// <summary>
/// <c>demo-reset</c>: run by the reset script right after it swapped the freshly seeded template
/// database in. It checks that the configured database holds the demonstration and only then records
/// the moment of the reset; a database that does not hold it is refused and the marker is left alone,
/// so the banner never announces a reset that did not leave a working demonstration.
/// </summary>
/// <remarks>
/// The reset itself is not done here. Swapping databases needs the API stopped, and the API is what
/// runs this binary.
/// </remarks>
public sealed class DemoResetCliCommand : ICliCommand
{
    /// <summary>Compares the database with the world.</summary>
    private readonly DemoSeedVerifier _verifier;

    /// <summary>Records the reset.</summary>
    private readonly DemoResetMarker _marker;

    /// <summary>Reads the instant.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the verb.</summary>
    /// <param name="verifier">Compares the database with the world.</param>
    /// <param name="marker">Records the reset.</param>
    /// <param name="clock">Reads the instant.</param>
    public DemoResetCliCommand(DemoSeedVerifier verifier, DemoResetMarker marker, IClock clock)
    {
        _verifier = verifier;
        _marker = marker;
        _clock = clock;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return "demo-reset"; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        if (invocation.Arguments.Count != 0)
        {
            await console.Error.WriteLineAsync("usage: demo-reset");
            return new CliResult(CliExitCode.Usage);
        }

        var problems = await _verifier.VerifyAsync(StandardDemoWorld.Instance, cancellationToken);
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                await console.Error.WriteLineAsync($"DemoSeedNotApplied: {problem}");
            }

            return new CliResult(CliExitCode.Refused);
        }

        var at = _clock.UtcNow;
        await _marker.WriteAsync(at, cancellationToken);
        await console.Output.WriteLineAsync($"demo reset recorded at {at:u}.");
        return new CliResult(CliExitCode.Success);
    }
}
