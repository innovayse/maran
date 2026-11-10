namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The three facts an audit entry may say about the package a request concerned: which module, which
/// version and which package digest. Nothing else about a package can be handed to the journal, because
/// there is nowhere in this shape to put it.
/// </summary>
/// <param name="ModuleId">The module's machine id.</param>
/// <param name="Version">The semantic version asked for, or null when the request names none (a removal).</param>
/// <param name="PackageSha256">The package digest as lower-case hexadecimal, or null when none is known yet.</param>
public sealed record ModulePackageReference(string ModuleId, string? Version, string? PackageSha256);
