using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Maran.Modules.Licensing.Domain.Interfaces;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// Behavioral contract of <see cref="LicenceStatusCliCommand"/>: it prints one line for the installed licence
/// (none installed is a line, not a failure), refuses arguments, and reads without writing anything.
/// </summary>
public sealed class LicenceStatusCliCommandTests
{
    /// <summary>Builds the command over a provider whose licence is absent.</summary>
    /// <returns>The command.</returns>
    private static LicenceStatusCliCommand CommandWithNoLicence()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IServerIdentitySource, NoMachineId>();
        services.AddSingleton<ILicenceLeaseSource, NoLease>();
        services.AddSingleton<Maran.Modules.Licensing.Interfaces.ILicenceRawTextSource, NoLicenceInstalledTextSource>();
        services.AddSingleton<Maran.Modules.Licensing.Interfaces.ILicenceSignatureVerifier>(
            new Ed25519LicenceSignatureVerifier(LicenceVerificationKey.None));
        services.AddSingleton<LicenceVerifier>();
        services.AddSingleton<LicenceCoverageSource>();
        services.AddScoped<LicenceStatusDisplayNames>();
        services.AddScoped<LicenceStatusLine>();

        return new LicenceStatusCliCommand(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>Builds the invocation.</summary>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(params string[] arguments)
    {
        return new CliInvocation("licence-status", arguments, false, false, null);
    }

    /// <summary>With no licence installed the command prints the "none installed" line and succeeds.</summary>
    [Fact]
    public async Task With_no_licence_installed_it_prints_one_line_and_succeeds()
    {
        using var console = new CapturedCliConsole();

        var result = await CommandWithNoLicence().ExecuteAsync(Invocation(), console.Console, default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        Assert.Equal("No licence installed", console.Output.Trim());
        Assert.Equal(string.Empty, console.Error);
    }

    /// <summary>An argument is a usage error and prints nothing to the output; no argument is the accepting control.</summary>
    [Fact]
    public async Task An_argument_is_a_usage_error()
    {
        using var console = new CapturedCliConsole();

        var result = await CommandWithNoLicence().ExecuteAsync(Invocation("--now"), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A clock fixed at one instant.</summary>
    /// <param name="UtcNow">The instant.</param>
    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;

    /// <summary>A host whose machine id cannot be read.</summary>
    private sealed class NoMachineId : IServerIdentitySource
    {
        /// <inheritdoc />
        public Task<string?> TryReadMachineIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>A lease source with no lease.</summary>
    private sealed class NoLease : ILicenceLeaseSource
    {
        /// <inheritdoc />
        public Task<LeaseReading> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<LeaseReading>(LeaseReading.Absent.Instance);
        }
    }
}
