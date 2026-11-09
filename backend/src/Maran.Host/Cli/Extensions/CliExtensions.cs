using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Services;
using Maran.Packaging.Options;
using Maran.Packaging.Services;

namespace Maran.Host.Cli.Extensions;

/// <summary>Registers the break-glass command-line mode of the panel binary.</summary>
public static class CliExtensions
{
    /// <summary>
    /// Adds the runner and every verb. Listed explicitly, one line per verb, so what the binary can be
    /// asked to do from a shell is visible here and not discovered by scanning.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelCli(this IServiceCollection services)
    {
        services.AddSingleton<CliSecretLiterals>();
        // The archive reader only: module-verify builds its package verifier from the key file it is handed,
        // so the container registers no IpmPackageVerifier, which would have no signature verifier to resolve.
        services.AddSingleton(IpmLimits.Default);
        services.AddSingleton<IpmReader>();
        services.AddSingleton<ICliCommand, MigrateCliCommand>();
        services.AddSingleton<ICliCommand, StatusCliCommand>();
        services.AddSingleton<ICliCommand, AdminResetPasswordCliCommand>();
        services.AddSingleton<ICliCommand, IssuePanelCertificateCliCommand>();
        services.AddSingleton<ICliCommand, ReportUpdateCliCommand>();
        services.AddSingleton<ICliCommand, UpdateRequestReportCliCommand>();
        services.AddSingleton<ICliCommand, RedactCliCommand>();
        services.AddSingleton<ICliCommand, OpenApiCliCommand>();
        services.AddSingleton<ICliCommand, ModuleManifestCliCommand>();
        services.AddSingleton<ICliCommand, ModuleVerifyCliCommand>();
        services.AddSingleton<CliRunner>();

        return services;
    }
}
