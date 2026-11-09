using Maran.Modules.Files.Domain.Enums;

namespace Maran.Modules.Files.Domain.Policies;

/// <summary>
/// Which archive format a file name stands for, and the suffix a format writes.
/// </summary>
/// <remarks>
/// The name decides only which format the panel ASKS the agent to read; the agent verifies the real
/// format of the bytes and refuses a mismatch, so a renamed file gains nothing from this.
/// </remarks>
public static class ArchiveFormatPolicy
{
    /// <summary>The suffix of a zip archive.</summary>
    public const string ZipSuffix = ".zip";

    /// <summary>The suffix of a gzip-compressed tar archive.</summary>
    public const string TarGzSuffix = ".tar.gz";

    /// <summary>Reads the format a file name stands for.</summary>
    /// <param name="fileName">The archive's path or name.</param>
    /// <param name="format">The format when the name carries a known suffix; otherwise zip.</param>
    /// <returns>True when the suffix is one the manager can extract.</returns>
    public static bool TryFromName(string fileName, out ArchiveFormatKind format)
    {
        format = ArchiveFormatKind.Zip;
        if (fileName.EndsWith(ZipSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.EndsWith(TarGzSuffix, StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            format = ArchiveFormatKind.TarGz;
            return true;
        }

        return false;
    }

    /// <summary>The suffix a format's archive is written with.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The suffix, with its leading dot.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A format this policy has no suffix for.</exception>
    public static string SuffixOf(ArchiveFormatKind format)
    {
        return format switch
        {
            ArchiveFormatKind.Zip => ZipSuffix,
            ArchiveFormatKind.TarGz => TarGzSuffix,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown archive format."),
        };
    }
}
