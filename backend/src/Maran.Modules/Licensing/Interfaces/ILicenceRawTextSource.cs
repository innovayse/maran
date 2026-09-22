namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Reads the currently installed licence envelope's raw text, or reports that none is installed.
/// </summary>
/// <remarks>
/// <para>
/// This slice was explicitly scoped without EF persistence: nothing in <c>Licensing/Persistence</c>
/// exists yet, so there is no database row or file path a real implementation could read from today.
/// The one implementation this slice ships, <see cref="Services.NoLicenceInstalledTextSource"/>,
/// always answers <c>null</c> — the honest answer for a build with no storage to check. The seam
/// exists anyway so the Host-level guarantee this slice must prove — "the panel starts regardless of
/// what the installed licence says" — can be exercised with something other than the one value this
/// build can ever produce: a test replaces this registration with a fake that returns malformed or
/// otherwise bad licence text and asserts the panel still starts (see
/// <c>Maran.Host.IntegrationTests</c>, the Licensing startup tests). A later slice that adds real
/// persistence implements this interface against it and nothing above this seam needs to change.
/// </para>
/// </remarks>
public interface ILicenceRawTextSource
{
    /// <summary>Reads the installed licence envelope's raw text.</summary>
    /// <returns>
    /// The raw text <see cref="Services.LicenceVerifier.VerifyAsync"/> expects, or <c>null</c> when
    /// nothing is installed.
    /// </returns>
    string? ReadRawLicenceText();
}
