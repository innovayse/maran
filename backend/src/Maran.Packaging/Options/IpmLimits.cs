namespace Maran.Packaging.Options;

/// <summary>
/// The caps the reader holds an archive to before it believes anything in it. A package is untrusted
/// bytes until its signature is checked, and a signature can only be checked after the manifest is
/// read, so the reader must be safe against a hostile archive on its own: these bounds are what keep a
/// small download from becoming an exhausted server (a decompression bomb) or a million-entry listing.
/// </summary>
/// <param name="MaxEntries">The most entries an archive may hold, the manifest and signature included.</param>
/// <param name="MaxEntryBytes">The most one entry may hold once unpacked.</param>
/// <param name="MaxTotalBytes">The most all entries together may hold once unpacked.</param>
/// <param name="MaxCompressionRatio">
/// The most an entry's unpacked size may exceed its stored size, as a multiple. Applied only to an entry
/// of at least <paramref name="RatioFloorBytes"/>, because a tiny entry of zeros compresses by a large
/// factor without being dangerous.
/// </param>
/// <param name="RatioFloorBytes">The unpacked size below which the ratio is not judged.</param>
/// <param name="MaxManifestBytes">The most the manifest entry may hold; read before anything else is believed.</param>
public sealed record IpmLimits(
    int MaxEntries,
    long MaxEntryBytes,
    long MaxTotalBytes,
    int MaxCompressionRatio,
    long RatioFloorBytes,
    long MaxManifestBytes)
{
    /// <summary>The caps a panel uses: 512 entries, 64 MiB an entry, 256 MiB in all, 100 to 1, judged from 1 MiB, a 1 MiB manifest.</summary>
    public static IpmLimits Default { get; } = new(
        MaxEntries: 512,
        MaxEntryBytes: 64L * 1024 * 1024,
        MaxTotalBytes: 256L * 1024 * 1024,
        MaxCompressionRatio: 100,
        RatioFloorBytes: 1024L * 1024,
        MaxManifestBytes: 1024L * 1024);
}
