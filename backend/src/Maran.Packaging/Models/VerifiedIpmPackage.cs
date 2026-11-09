namespace Maran.Packaging.Models;

/// <summary>
/// A package every check passed for: the archive is safe, the signature is the supplied key's over the
/// manifest, the manifest obeys the module rules and fits the panel, and each payload byte is the one
/// the manifest pins. It is evidence about the package, not a sandbox: a signed module is trusted code.
/// </summary>
/// <param name="Manifest">The signed manifest.</param>
/// <param name="Archive">The archive whose payload the manifest pins.</param>
public sealed record VerifiedIpmPackage(IpmManifest Manifest, IpmArchive Archive);
