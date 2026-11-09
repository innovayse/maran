using Maran.Agent.Client.Interfaces;
using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Sites.Commands.InstallPhpVersion;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Installs the PHP versions the world's sites run on that the server does not already carry, through
/// the same command the version screen uses.
/// </summary>
public sealed class PhpVersionSeedStep : IDemoSeedStep
{
    /// <summary>Asks the server which versions it carries.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="php">Asks the server which versions it carries.</param>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public PhpVersionSeedStep(IAgentPhpClient php, DemoSeedCommandRunner runner)
    {
        _php = php;
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "php-versions"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var installed = await _php.ListVersionsAsync(cancellationToken);
        if (!installed.IsSuccess)
        {
            throw new DemoSeedException(Name, installed.Error!.Code);
        }

        var carried = installed.Value.Select(version => { return version.Version; }).ToHashSet(StringComparer.Ordinal);
        foreach (var version in context.World.PhpVersions.Where(version => { return !carried.Contains(version); }))
        {
            await _runner.RunAsync<IReadOnlyList<Maran.Modules.Sites.Common.PhpVersionDto>>(
                Name,
                new InstallPhpVersionCommand(version, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
        }
    }
}
