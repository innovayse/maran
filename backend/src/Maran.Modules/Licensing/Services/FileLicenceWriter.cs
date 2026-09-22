using System.Text;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.SharedKernel.Utilities.IO;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Installs the licence artefact at <see cref="LicenceStorageOptions.LicenceFilePath"/> with a single
/// atomic <c>rename()</c>, per <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-
/// note.md</c> §2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never delete-then-write, never an in-place overwrite.</b> Both are named and rejected by the
/// threat note for the identical reason: either can make a reader observe something that is neither
/// the old file nor the new one — a false <c>Absent</c>, or a spliced document. This type delegates
/// the actual write to <see cref="AtomicFileWriter"/>, which writes a temp file in the SAME directory,
/// fsyncs it, then renames it over the live path — the one step in this whole surface that has no
/// intermediate state a concurrent reader could ever observe.
/// </para>
/// <para>
/// <b>Mode <c>0640</c>, owner read/write, group read, world nothing</b> — the threat note's §1
/// reasoning: not <c>0600</c>, because a future read-only tool running as a member of the
/// <c>maran</c> group should not need the exact uid that installed the licence; not world-readable,
/// because a licence's payload names the installed tier and module list, which is operational detail
/// about what a customer pays for, the same reason <c>LicensingController</c> itself is
/// administrator-only.
/// </para>
/// </remarks>
public sealed class FileLicenceWriter : ILicenceWriter
{
    /// <summary><c>0640</c>: owner read/write, group read, world nothing. See this type's own remarks.</summary>
    private const UnixFileMode LicenceFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    /// <summary>Where the installed licence artefact lives — see <see cref="LicenceStorageOptions"/>'s own remarks.</summary>
    private readonly LicenceStorageOptions _options;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">Where the installed licence artefact lives.</param>
    public FileLicenceWriter(IOptions<LicenceStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task InstallAsync(string rawLicenceText, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawLicenceText);

        var directory = Path.GetDirectoryName(_options.LicenceFilePath)!;
        Directory.CreateDirectory(directory);

        var bytes = Encoding.UTF8.GetBytes(rawLicenceText);

        await AtomicFileWriter.WriteAsync(_options.LicenceFilePath, bytes, LicenceFileMode, cancellationToken);
    }
}
