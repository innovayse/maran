namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Installs or replaces the licence artefact this build's <see cref="ILicenceRawTextSource"/> will
/// read back afterward. The only writer of that one path (spec §228's install half; see
/// <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c> §1).
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ILicenceRawTextSource"/>, which is read-only: no caller of
/// the read seam may ever also write through it by accident, and the write seam takes only bytes
/// that have ALREADY verified <c>Valid</c> — see
/// <c>Commands.InstallLicence.InstallLicenceCommandHandler</c>'s own remarks for why persistence is
/// never reached for anything less.
/// </remarks>
public interface ILicenceWriter
{
    /// <summary>Writes the given raw licence text as the installed artefact, replacing any previous one.</summary>
    /// <param name="rawLicenceText">
    /// The exact bytes that already verified <see cref="Domain.Enums.LicenceStatus.Valid"/> — never
    /// written for anything less.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>
    /// A task that completes once the new artefact is durably, atomically in place. On any failure —
    /// including cancellation — the previous artefact, if any, is left exactly as it was; see this
    /// interface's implementation for the atomicity guarantee.
    /// </returns>
    Task InstallAsync(string rawLicenceText, CancellationToken cancellationToken);
}
