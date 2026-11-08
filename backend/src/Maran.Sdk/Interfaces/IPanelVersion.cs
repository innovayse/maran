namespace Maran.Sdk.Interfaces;

/// <summary>
/// The version of the running panel, as a read-only window for anything that must compare it with a
/// module's <see cref="Contracts.Manifest.MinPanelVersion"/>.
/// </summary>
/// <remarks>
/// Implemented by the Host, which is the only project that knows the build it is part of. A window and
/// not a constant, so a test composes any panel version it likes and the answer is not read from the
/// ambient assembly.
/// </remarks>
public interface IPanelVersion
{
    /// <summary>
    /// The panel's version, a semantic version in the grammar of
    /// <c>Maran.SharedKernel.Utilities.Versions.SemanticVersion</c> (no revision suffix).
    /// </summary>
    string Current { get; }
}
