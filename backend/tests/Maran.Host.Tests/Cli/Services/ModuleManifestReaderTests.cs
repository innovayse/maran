using Maran.Host.Cli.Services;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Services;

/// <summary>
/// Behavioral contract of <see cref="ModuleManifestReader"/> for a module whose manifest getter is broken:
/// the operator gets a sentence naming the module, never a stack trace, and a healthy module beside it is
/// still read.
/// </summary>
public sealed class ModuleManifestReaderTests
{
    /// <summary>A manifest getter that throws is answered with a sentence that carries no exception text.</summary>
    [Fact]
    public void A_manifest_getter_that_throws_is_answered_with_a_sentence()
    {
        var reading = ModuleManifestReader.Read([typeof(ThrowingManifestModule)], "Throwing.dll");

        Assert.Null(reading.Manifest);
        Assert.Equal(
            "ThrowingManifestModule could not state its manifest: InvalidOperationException.",
            reading.Failure);
        Assert.DoesNotContain("not available", reading.Failure, StringComparison.Ordinal);
    }

    /// <summary>A manifest getter that answers null is answered with a sentence.</summary>
    [Fact]
    public void A_manifest_getter_that_answers_null_is_answered_with_a_sentence()
    {
        var reading = ModuleManifestReader.Read([typeof(NullManifestModule)], "Null.dll");

        Assert.Null(reading.Manifest);
        Assert.Equal("NullManifestModule publishes no manifest.", reading.Failure);
    }

    /// <summary>A module with a readable manifest is read (accepting control).</summary>
    [Fact]
    public void A_module_with_a_readable_manifest_is_read()
    {
        var reading = ModuleManifestReader.Read([typeof(HealthyModule)], "Healthy.dll");

        Assert.Null(reading.Failure);
        Assert.NotNull(reading.Manifest);
        Assert.Equal("healthy", reading.Manifest.Id);
        Assert.Equal("Healthy.dll", reading.Manifest.EntryAssembly);
    }

    /// <summary>A module whose manifest getter throws.</summary>
    private sealed class ThrowingManifestModule : IPanelModule
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "throwing"; }
        }

        /// <inheritdoc />
        public Manifest Manifest
        {
            get { throw new InvalidOperationException("the manifest is not available"); }
        }

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Never composed: the reader only asks for the manifest.
        }
    }

    /// <summary>A module whose manifest getter answers null.</summary>
    private sealed class NullManifestModule : IPanelModule
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "null-manifest"; }
        }

        /// <inheritdoc />
        public Manifest Manifest
        {
            get { return null!; }
        }

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Never composed: the reader only asks for the manifest.
        }
    }

    /// <summary>A packageable module with a readable manifest, owning no schema.</summary>
    private sealed class HealthyModule : IPanelModule
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "healthy"; }
        }

        /// <inheritdoc />
        public Manifest Manifest
        {
            get { return TestManifests.Of("healthy"); }
        }

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Never composed: the reader only asks for the manifest.
        }
    }
}
