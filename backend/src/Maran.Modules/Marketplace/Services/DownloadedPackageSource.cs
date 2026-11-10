using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// A package downloaded from the marketplace and held in a private temp file, handed to the loader as a
/// verified source: it carries what the signed catalogue promised, so the loader can refuse a package
/// whose own manifest disagrees. The file is deleted when this object is disposed, whether or not the
/// loader accepted the package.
/// </summary>
/// <remarks>
/// The caller owns the lifetime: <c>await using</c> around the install. Disposal deletes the file and
/// lets a failure to delete propagate, because a package left on disk is a thing the administrator must
/// hear about, not a quiet leak.
/// </remarks>
public sealed class DownloadedPackageSource : IVerifiedModulePackageSource, IAsyncDisposable
{
    /// <summary>The temp file's absolute path.</summary>
    private readonly string _path;

    /// <summary>Whether the file has been deleted.</summary>
    private bool _disposed;

    /// <inheritdoc />
    public ExpectedModulePackage Expected { get; }

    /// <inheritdoc />
    public string Origin { get; }

    /// <summary>Wraps a downloaded file.</summary>
    /// <param name="path">The temp file's absolute path.</param>
    /// <param name="expected">What the catalogue promised about the package.</param>
    /// <param name="origin">Where the bytes came from, recorded with the installed module.</param>
    public DownloadedPackageSource(string path, ExpectedModulePackage expected, string origin)
    {
        _path = path;
        Expected = expected;
        Origin = origin;
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stream stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    /// <summary>Deletes the temp file. Safe to call more than once.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            File.Delete(_path);
        }

        return ValueTask.CompletedTask;
    }
}
