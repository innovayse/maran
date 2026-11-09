using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Demo.Seeding;
using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World;

namespace Maran.Host.Demo.Cli;

/// <summary>
/// <c>demo-seed</c>: builds the demonstration in the configured database and checks it against the
/// world. The database must be migrated and empty; the reset script points this at the template
/// database, never at the live one.
/// </summary>
public sealed class DemoSeedCliCommand : ICliCommand
{
    /// <summary>The verb's name on the command line.</summary>
    public const string VerbName = "demo-seed";

    /// <summary>Builds the world.</summary>
    private readonly DemoSeeder _seeder;

    /// <summary>Compares the database with the world.</summary>
    private readonly DemoSeedVerifier _verifier;

    /// <summary>Starts the message runtime for the run.</summary>
    private readonly DemoMessagingLifetime _messaging;

    /// <summary>Creates the verb.</summary>
    /// <param name="seeder">Builds the world.</param>
    /// <param name="verifier">Compares the database with the world.</param>
    /// <param name="messaging">Starts the message runtime for the run.</param>
    public DemoSeedCliCommand(DemoSeeder seeder, DemoSeedVerifier verifier, DemoMessagingLifetime messaging)
    {
        _seeder = seeder;
        _verifier = verifier;
        _messaging = messaging;
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
            await console.Error.WriteLineAsync("usage: demo-seed");
            return new CliResult(CliExitCode.Usage);
        }

        IReadOnlyList<string> problems = [];
        try
        {
            await _messaging.RunAsync(
                async () =>
                {
                    await _seeder.SeedAsync(console.Output, cancellationToken);
                    problems = await _verifier.VerifyAsync(StandardDemoWorld.Instance, cancellationToken);
                },
                cancellationToken);
        }
        catch (DemoSeedException exception)
        {
            await console.Error.WriteLineAsync(exception.Message);
            return new CliResult(CliExitCode.Failure);
        }

        foreach (var problem in problems)
        {
            await console.Error.WriteLineAsync($"not seeded as defined: {problem}");
        }

        if (problems.Count > 0)
        {
            return new CliResult(CliExitCode.Failure);
        }

        await console.Output.WriteLineAsync(
            $"demo seeded: {StandardDemoWorld.Instance.Accounts.Count} accounts, verified against the world.");
        return new CliResult(CliExitCode.Success);
    }
}
