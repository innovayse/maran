using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Modules.Fixtures;

/// <summary>
/// A module whose manifest a test hands it, standing in for a module that arrived in a package. It
/// lives in the test assembly, which is how a test makes two modules share an assembly.
/// </summary>
internal sealed class StubPackagedModule : IPanelModule
{
    /// <summary>Creates the stand-in.</summary>
    /// <param name="manifest">The manifest it publishes.</param>
    public StubPackagedModule(Manifest manifest)
    {
        Manifest = manifest;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return Manifest.Id; }
    }

    /// <inheritdoc />
    public Manifest Manifest { get; }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Nothing to register: the stand-in is inspected, never composed into a running panel.
    }
}
