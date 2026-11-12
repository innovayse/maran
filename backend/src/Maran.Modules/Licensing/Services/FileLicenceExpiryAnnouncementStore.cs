using System.Text.Json;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Maran.SharedKernel.Utilities.IO;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Keeps the last expiry announcement in a small JSON file beside the licence
/// (<see cref="LicenceStorageOptions.ExpiryAnnouncementFilePath"/>), written with one atomic rename like the licence
/// and the lease.
/// </summary>
/// <remarks>
/// <para>
/// <b>A file and not a table.</b> The module owns no database: the licence, the lease and this note are all state of
/// the SERVER, kept as files in the state directory, and a licence problem must never depend on the panel's database
/// being reachable. The note holds an expiry instant and a number of days, nothing that identifies the machine.
/// </para>
/// <para>
/// <b>Reading is forgiving, writing is not.</b> A missing, oversized, malformed or unreadable file is "nothing
/// announced": the worst outcome is one repeated notice. Mode <c>0640</c> like its neighbours.
/// </para>
/// </remarks>
public sealed class FileLicenceExpiryAnnouncementStore : ILicenceExpiryAnnouncementStore
{
    /// <summary>The largest note the store reads, in bytes; the real one is under a hundred.</summary>
    public const int MaxFileBytes = 1024;

    /// <summary><c>0640</c>: owner read/write, group read, world nothing.</summary>
    private const UnixFileMode AnnouncementFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    /// <summary>Where the note lives.</summary>
    private readonly LicenceStorageOptions _options;

    /// <summary>Creates the store.</summary>
    /// <param name="options">Where the note lives.</param>
    public FileLicenceExpiryAnnouncementStore(IOptions<LicenceStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<LicenceExpiryAnnouncement?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(_options.ExpiryAnnouncementFilePath);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return null;
            }

            await using var stream = File.OpenRead(info.FullName);
            var stored = await JsonSerializer.DeserializeAsync<StoredAnnouncement>(stream, cancellationToken: cancellationToken);

            return stored is { Expiry: { } expiry, ThresholdDays: { } days and >= 1 }
                ? new LicenceExpiryAnnouncement(expiry, days)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RememberAsync(LicenceExpiryAnnouncement announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.ExpiryAnnouncementFilePath))!;
        Directory.CreateDirectory(directory);

        await AtomicFileWriter.WriteAsync(
            _options.ExpiryAnnouncementFilePath,
            JsonSerializer.SerializeToUtf8Bytes(new StoredAnnouncement(announcement.Expiry, announcement.ThresholdDays)),
            AnnouncementFileMode,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ForgetAsync(CancellationToken cancellationToken)
    {
        File.Delete(_options.ExpiryAnnouncementFilePath);

        return Task.CompletedTask;
    }

    /// <summary>The note as it is written; nullable members so a note missing a part is read as no note.</summary>
    /// <param name="Expiry">The announced licence's expiry.</param>
    /// <param name="ThresholdDays">The announced threshold.</param>
    private sealed record StoredAnnouncement(DateTimeOffset? Expiry, int? ThresholdDays);
}
