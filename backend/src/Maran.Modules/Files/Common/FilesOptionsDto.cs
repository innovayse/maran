using Maran.Modules.Files.Domain.Enums;

namespace Maran.Modules.Files.Common;

/// <summary>The limits the server enforces, so the SPA holds no constant of its own.</summary>
/// <param name="MaxUploadBytes">The largest file an upload may carry, in bytes.</param>
/// <param name="MaxEditBytes">The largest file the text editor opens, in bytes.</param>
/// <param name="MaxDownloadBytes">The largest file a download serves, in bytes.</param>
/// <param name="MaxBatchItems">The most items one batch request may name.</param>
/// <param name="TrashRetentionDays">How many days an entry stays in the trash.</param>
/// <param name="ExtractMaxEntries">The most entries an extraction writes before the agent stops it.</param>
/// <param name="ExtractMaxBytes">The most bytes an extraction writes before the agent stops it.</param>
/// <param name="ExtractMaxRatio">The highest ratio of extracted to archive size the agent tolerates.</param>
/// <param name="ArchiveFormats">The formats an archive can be written in; wire names are <c>zip</c> and <c>tar.gz</c>.</param>
public sealed record FilesOptionsDto(
    long MaxUploadBytes,
    long MaxEditBytes,
    long MaxDownloadBytes,
    int MaxBatchItems,
    int TrashRetentionDays,
    long ExtractMaxEntries,
    long ExtractMaxBytes,
    int ExtractMaxRatio,
    IReadOnlyList<ArchiveFormatKind> ArchiveFormats);
