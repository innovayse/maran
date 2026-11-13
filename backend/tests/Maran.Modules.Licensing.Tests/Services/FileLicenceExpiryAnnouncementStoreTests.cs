using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="FileLicenceExpiryAnnouncementStore"/>: what was announced survives, and a note that cannot be
/// trusted reads as no note (the safe direction: one repeated notice, never a missed one).
/// </summary>
public sealed class FileLicenceExpiryAnnouncementStoreTests : IDisposable
{
    /// <summary>A directory of its own, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "announce-store-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The announcement the tests store.</summary>
    private static readonly LicenceExpiryAnnouncement Sample = new(new DateTimeOffset(2026, 12, 26, 22, 0, 0, TimeSpan.Zero), 14);

    /// <summary>The note's path.</summary>
    private string NotePath
    {
        get { return Path.Combine(_directory, "nested", "note.json"); }
    }

    /// <summary>Builds the store over the note's path.</summary>
    /// <returns>The store.</returns>
    private FileLicenceExpiryAnnouncementStore Store()
    {
        return new FileLicenceExpiryAnnouncementStore(
            new OptionsWrapper<LicenceStorageOptions>(
                new LicenceStorageOptions { ExpiryAnnouncementFilePath = NotePath }));
    }

    /// <summary>What is stored is read back exactly, by a store that never wrote it; a missing note reads as none.</summary>
    [Fact]
    public async Task A_stored_announcement_is_read_back_and_a_missing_one_is_none()
    {
        Assert.Null(await Store().ReadAsync(CancellationToken.None));

        await Store().RememberAsync(Sample, CancellationToken.None);

        Assert.Equal(Sample, await Store().ReadAsync(CancellationToken.None));
    }

    /// <summary>A forgotten announcement reads as none, and forgetting when nothing is stored is not an error.</summary>
    [Fact]
    public async Task A_forgotten_announcement_reads_as_none()
    {
        await Store().RememberAsync(Sample, CancellationToken.None);

        await Store().ForgetAsync(CancellationToken.None);
        await Store().ForgetAsync(CancellationToken.None);

        Assert.Null(await Store().ReadAsync(CancellationToken.None));
    }

    /// <summary>A note that is malformed, incomplete or oversized reads as none; a good note beside them is the control.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Expiry\":\"2026-12-26T22:00:00+00:00\"}")]
    [InlineData("{\"Expiry\":\"2026-12-26T22:00:00+00:00\",\"ThresholdDays\":0}")]
    public async Task A_note_that_cannot_be_trusted_reads_as_none(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NotePath)!);
        await File.WriteAllTextAsync(NotePath, text);

        Assert.Null(await Store().ReadAsync(CancellationToken.None));

        await Store().RememberAsync(Sample, CancellationToken.None);
        Assert.Equal(Sample, await Store().ReadAsync(CancellationToken.None));
    }

    /// <summary>A note larger than the cap is not read.</summary>
    [Fact]
    public async Task An_oversized_note_reads_as_none()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NotePath)!);
        await File.WriteAllTextAsync(
            NotePath,
            "{\"Expiry\":\"2026-12-26T22:00:00+00:00\",\"ThresholdDays\":14,\"Padding\":\""
            + new string('x', FileLicenceExpiryAnnouncementStore.MaxFileBytes)
            + "\"}");

        Assert.Null(await Store().ReadAsync(CancellationToken.None));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
