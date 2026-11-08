using Maran.Packaging.Models;

namespace Maran.Host.Cli.Models;

/// <summary>
/// What reading a module assembly's manifest produced: the manifest in the package's form, or the one
/// sentence saying why the assembly cannot be packaged. Exactly one of the two is set.
/// </summary>
/// <param name="Manifest">The package manifest, without its file listing and interface bundle; null on failure.</param>
/// <param name="Failure">Why the assembly cannot be packaged, for the operator's terminal; null on success.</param>
public sealed record ModuleManifestReading(IpmManifest? Manifest, string? Failure);
