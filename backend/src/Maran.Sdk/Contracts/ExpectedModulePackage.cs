namespace Maran.Sdk.Contracts;

/// <summary>
/// What the marketplace's signed catalogue promised about a package before it was downloaded. The
/// loader compares it with the package's own signed manifest and refuses a package that disagrees, so a
/// catalogue entry and the bytes behind it cannot quietly diverge (a mirror serving another module's
/// package under this one's name).
/// </summary>
/// <param name="ModuleId">The module's <see cref="Manifest.Id"/> the catalogue named.</param>
/// <param name="Version">The semantic version the catalogue named.</param>
/// <param name="Sha256">The lower-case hexadecimal SHA-256 of the whole package file, as the catalogue declared it.</param>
/// <param name="SizeBytes">The package file's size in bytes, as the catalogue declared it.</param>
public sealed record ExpectedModulePackage(string ModuleId, string Version, string Sha256, long SizeBytes);
