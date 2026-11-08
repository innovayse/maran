using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Queries.GetFilesOptions;

/// <summary>Handles <see cref="GetFilesOptionsQuery"/> by restating the module's configured limits.</summary>
/// <remarks>
/// The limits are read from <see cref="IOptionsMonitor{TOptions}"/> at each call, so a reloaded setting
/// reaches the interface at its next load and the numbers shown are the numbers the routes enforce.
/// The archive formats are every member of <see cref="ArchiveFormatKind"/>.
/// </remarks>
public sealed class GetFilesOptionsQueryHandler
{
    /// <summary>The limits as configured.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Creates the handler.</summary>
    /// <param name="options">The limits as configured.</param>
    public GetFilesOptionsQueryHandler(IOptionsMonitor<FilesOptions> options)
    {
        _options = options;
    }

    /// <summary>Returns the limits and choices.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying them; this operation never fails.</returns>
    public Task<Result<FilesOptionsDto>> HandleAsync(GetFilesOptionsQuery query, CancellationToken cancellationToken)
    {
        var limits = _options.CurrentValue;

        return Task.FromResult(
            Result<FilesOptionsDto>.Ok(
                new FilesOptionsDto(
                    limits.MaxUploadBytes,
                    limits.MaxEditBytes,
                    limits.MaxDownloadBytes,
                    limits.MaxBatchItems,
                    limits.TrashRetentionDays,
                    limits.ExtractMaxEntries,
                    limits.ExtractMaxBytes,
                    limits.ExtractMaxRatio,
                    Enum.GetValues<ArchiveFormatKind>())));
    }
}
