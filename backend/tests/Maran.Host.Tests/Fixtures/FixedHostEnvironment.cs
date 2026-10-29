using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Fixtures;

/// <summary>
/// A host environment that answers one fixed environment name, so a test can ask the options
/// validators what they decide in <c>Production</c> or <c>Staging</c> without booting a host.
/// </summary>
public sealed class FixedHostEnvironment : IHostEnvironment
{
    /// <summary>Creates an environment that reports <paramref name="environmentName"/>.</summary>
    /// <param name="environmentName">The environment name, exactly as a host would report it.</param>
    public FixedHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
    }

    /// <inheritdoc />
    public string EnvironmentName { get; set; }

    /// <inheritdoc />
    public string ApplicationName { get; set; } = "Maran.Host.Tests";

    /// <inheritdoc />
    public string ContentRootPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
