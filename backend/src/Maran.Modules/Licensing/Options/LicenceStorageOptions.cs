using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Licensing.Options;

/// <summary>
/// Where the installed licence artefact lives on disk. Bound from the <c>Licensing</c> configuration
/// section; a server's own value is never set — the default is the one path the threat note settles
/// on (<c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c> §1) — but the
/// seam exists so a test can point it at an isolated temp directory instead of the real,
/// root-adjacent, systemd-hardened path no unprivileged test process can write to.
/// </summary>
/// <remarks>
/// The same shape <c>Ssl.Options.AcmeOptions.CertificateStorePath</c> already uses for the identical
/// reason: a hard-coded path could not be exercised end to end in a test process, and hiding the path
/// behind options rather than a bare <c>const</c> is what makes that possible without touching
/// production wiring.
/// </remarks>
public sealed class LicenceStorageOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Licensing";

    /// <summary>The installed licence artefact's absolute path.</summary>
    [Required]
    [MinLength(1)]
    public string LicenceFilePath { get; set; } = "/var/lib/maran/licence.json";
}
