using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads the installed licence's raw text from <see cref="LicenceStorageOptions.LicenceFilePath"/>, the
/// same path <see cref="FileLicenceWriter"/> installs to.
/// </summary>
/// <remarks>
/// <para>
/// This is the real implementation of the seam <c>ILicenceRawTextSource</c>'s own remarks describe
/// as still-missing: "nothing in <c>Licensing/Persistence</c> exists yet" was true for the read-only
/// slice; this type replaces <see cref="NoLicenceInstalledTextSource"/> in
/// <see cref="LicensingModule"/>'s registration once the install endpoint gives it something a real
/// file could actually contain.
/// </para>
/// <para>
/// <b>A missing file is <c>Absent</c>, not an error.</b> This is the ordinary state on a fresh
/// install and on every host that has never run the install endpoint — see
/// <c>Domain.Enums.LicenceStatus</c>'s own remarks for why that distinction matters. Any other read
/// failure (permission, a transient I/O error) is reported the same way, for the identical §229
/// reason <see cref="Services.LicenceVerifier"/> itself never throws: a storage hiccup reading this
/// file must degrade to "nothing installed," never crash the caller — <see cref="LicenceVerifier"/>
/// downstream then reports whatever this method returns as either <c>Absent</c> (on <c>null</c>) or
/// verifies real bytes; there is no third path back up through this seam for an exception to take.
/// </para>
/// </remarks>
public sealed class FileLicenceRawTextSource : ILicenceRawTextSource
{
    /// <summary>Where the installed licence artefact lives — see <see cref="LicenceStorageOptions"/>'s own remarks.</summary>
    private readonly LicenceStorageOptions _options;

    /// <summary>Creates the source.</summary>
    /// <param name="options">Where the installed licence artefact lives.</param>
    public FileLicenceRawTextSource(IOptions<LicenceStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    /// <returns>
    /// The installed licence's raw text, or <c>null</c> when nothing is installed or the file could
    /// not be read.
    /// </returns>
    public string? ReadRawLicenceText()
    {
        try
        {
            return File.Exists(_options.LicenceFilePath) ? File.ReadAllText(_options.LicenceFilePath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
