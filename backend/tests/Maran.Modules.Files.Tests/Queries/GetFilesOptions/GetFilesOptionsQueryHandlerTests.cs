using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Queries.GetFilesOptions;
using Maran.Modules.Files.Tests.TestSupport;

namespace Maran.Modules.Files.Tests.Queries.GetFilesOptions;

/// <summary>That the limits the interface shows are the limits the module is configured with.</summary>
public sealed class GetFilesOptionsQueryHandlerTests
{
    /// <summary>Every figure is the configured one, so a changed setting changes what the screens show.</summary>
    [Fact]
    public async Task Every_figure_is_the_configured_one()
    {
        var configured = new FilesOptions
        {
            MaxUploadBytes = 11,
            MaxEditBytes = 12,
            MaxDownloadBytes = 13,
            MaxBatchItems = 14,
            TrashRetentionDays = 15,
            ExtractMaxEntries = 16,
            ExtractMaxBytes = 17,
            ExtractMaxRatio = 18,
        };
        var handler = new GetFilesOptionsQueryHandler(new FixedOptionsMonitor<FilesOptions>(configured));

        var result = await handler.HandleAsync(new GetFilesOptionsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value.MaxUploadBytes);
        Assert.Equal(12, result.Value.MaxEditBytes);
        Assert.Equal(13, result.Value.MaxDownloadBytes);
        Assert.Equal(14, result.Value.MaxBatchItems);
        Assert.Equal(15, result.Value.TrashRetentionDays);
        Assert.Equal(16, result.Value.ExtractMaxEntries);
        Assert.Equal(17, result.Value.ExtractMaxBytes);
        Assert.Equal(18, result.Value.ExtractMaxRatio);
    }

    /// <summary>The archive formats offered are every format the module knows, and none is invented.</summary>
    [Fact]
    public async Task The_archive_formats_offered_are_every_format_the_module_knows()
    {
        var handler = new GetFilesOptionsQueryHandler(new FixedOptionsMonitor<FilesOptions>(new FilesOptions()));

        var result = await handler.HandleAsync(new GetFilesOptionsQuery(), default);

        Assert.Equal(Enum.GetValues<ArchiveFormatKind>(), result.Value.ArchiveFormats);
        Assert.NotEmpty(result.Value.ArchiveFormats);
    }
}
