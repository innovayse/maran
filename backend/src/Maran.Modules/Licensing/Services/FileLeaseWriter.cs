using System.Text;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.SharedKernel.Utilities.IO;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Writes the lease file with a single atomic rename, the same writer and the same mode as the licence file
/// (<see cref="FileLicenceWriter"/>): a reader sees the old lease complete or the new one complete, never
/// absence and never a mixture, so a renewal that dies half way cannot lapse the paid modules.
/// </summary>
/// <remarks>
/// Mode <c>0640</c>: owner read/write, group read, nothing for the world. The lease is a bearer credential
/// for the licence-bound services until it ends (<see cref="Domain.Policies.LeaseBearerToken"/>), so it is
/// kept as private as the licence beside it. It is written ONLY after <see cref="LeaseRenewer"/> has judged
/// the text; this type does not judge.
/// </remarks>
public sealed class FileLeaseWriter : ILeaseWriter
{
    /// <summary><c>0640</c>: owner read/write, group read, world nothing.</summary>
    private const UnixFileMode LeaseFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    /// <summary>Where the lease file lives.</summary>
    private readonly LeaseStorageOptions _options;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">Where the lease file lives.</param>
    public FileLeaseWriter(IOptions<LeaseStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string envelopeText, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeText);

        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.LeaseFilePath))!;
        Directory.CreateDirectory(directory);

        await AtomicFileWriter.WriteAsync(
            _options.LeaseFilePath,
            Encoding.UTF8.GetBytes(envelopeText),
            LeaseFileMode,
            cancellationToken);
    }
}
