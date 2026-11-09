namespace Maran.Sdk.Interfaces;

/// <summary>
/// Where a module package's bytes come from. The loader reads the package from here and nowhere else,
/// so a file the operator supplied and a download are the same thing to it, and a test supplies a
/// package from memory.
/// </summary>
public interface IModulePackageSource
{
    /// <summary>A human-readable description of where the bytes come from, recorded with the module.</summary>
    string Origin { get; }

    /// <summary>Opens the package for reading; the caller disposes the stream.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A readable stream positioned at the start of the package.</returns>
    Task<Stream> OpenAsync(CancellationToken ct);
}
