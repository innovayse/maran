using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Maran.Modules.Licensing.Domain.Interfaces;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// Behavioral contract of <see cref="LicenceStatusCliCommand"/>: it prints one line for the installed licence
/// (none installed is a line, not a failure) and one for this server's id, refuses arguments, and reads without
/// writing anything.
/// </summary>
public sealed class LicenceStatusCliCommandTests
{
    /// <summary>A machine id as the marketplace spells one.</summary>
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    /// <summary>Builds the command over a provider whose licence is absent.</summary>
    /// <param name="machineId">The machine id the host reports, or null for a host whose id cannot be read.</param>
    /// <returns>The command.</returns>
    private static LicenceStatusCliCommand CommandWithNoLicence(string? machineId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IServerIdentitySource>(new HostMachineId(machineId));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new LicenceClaimOptions()));
        services.AddSingleton<LicenceServerFactsSource>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new LicenceExpiryWarningOptions()));
        services.AddSingleton<LicenceExpiryWarningSource>();
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

    /// <summary>With no licence installed the command prints the "none installed" line, then the server line, and succeeds.</summary>
    [Fact]
    public async Task With_no_licence_installed_it_prints_the_licence_line_and_the_server_line_and_succeeds()
    {
        using var console = new CapturedCliConsole();

        var result = await CommandWithNoLicence(ServerId).ExecuteAsync(Invocation(), console.Console, default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        Assert.Equal(["No licence installed", "Server ID: " + ServerId], Lines(console.Output));
        Assert.Equal(string.Empty, console.Error);
    }

    /// <summary>A host whose id cannot be read still prints the line, saying so; the case above is the control.</summary>
    [Fact]
    public async Task A_host_whose_id_cannot_be_read_prints_unavailable_for_the_server()
    {
        using var console = new CapturedCliConsole();

        var result = await CommandWithNoLicence().ExecuteAsync(Invocation(), console.Console, default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        Assert.Equal(["No licence installed", "Server ID: unavailable"], Lines(console.Output));
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

    /// <summary>Splits the command's output into its lines.</summary>
    /// <param name="output">Everything the command printed.</param>
    /// <returns>The non-empty lines, in order.</returns>
    private static string[] Lines(string output)
    {
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>A clock fixed at one instant.</summary>
    /// <param name="UtcNow">The instant.</param>
    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;

    /// <summary>A host that reports one machine id, or none.</summary>
    /// <param name="MachineId">The id, or null for a host whose id cannot be read.</param>
    private sealed record HostMachineId(string? MachineId) : IServerIdentitySource
    {
        /// <inheritdoc />
        public Task<string?> TryReadMachineIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(MachineId);
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
