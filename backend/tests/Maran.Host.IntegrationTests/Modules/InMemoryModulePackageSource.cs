using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>A package source that holds its bytes in memory, so a test supplies a package with no file and no network.</summary>
public sealed class InMemoryModulePackageSource : IModulePackageSource
{
    /// <summary>The package bytes, exactly as a file would hold them.</summary>
    private readonly byte[] _bytes;

    /// <summary>Creates the source.</summary>
    /// <param name="bytes">The package bytes.</param>
    /// <param name="origin">What the loader records as where the package came from.</param>
    public InMemoryModulePackageSource(byte[] bytes, string origin = "memory")
    {
        _bytes = bytes;
        Origin = origin;
    }

    /// <inheritdoc />
    public string Origin { get; }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(CancellationToken ct)
    {
        return Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
    }
}
