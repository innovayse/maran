using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Mappers;
using AgentFileEntry = Maran.Agent.Client.Services.FilesService.FileEntryDto;
using AgentFileEntryKind = Maran.Agent.Client.Services.FilesService.FileEntryKind;

namespace Maran.Modules.Files.Tests.Mappers;

/// <summary>Holds <see cref="FileEntryMapper"/> to restating what the agent said and nothing more.</summary>
public sealed class FileEntryMapperTests
{
    /// <summary>When the entry was modified.</summary>
    private static readonly DateTimeOffset Modified = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An entry is restated with its path, octal mode and kind name.</summary>
    [Fact]
    public void An_entry_is_restated_with_its_path_and_octal_mode()
    {
        var row = FileEntryMapper.From(
            new AgentFileEntry("index.php", AgentFileEntryKind.File, 12, Convert.ToUInt32("100644", 8), Modified, 1000, 1000, 1),
            "public_html",
            "File");

        Assert.Equal("public_html/index.php", row.Path);
        Assert.Equal("644", row.Mode);
        Assert.Equal(FileEntryKind.File, row.Kind);
        Assert.Equal("File", row.KindName);
        Assert.False(row.IsSymlink);
    }

    /// <summary>An entry in the home has no folder prefix.</summary>
    [Fact]
    public void An_entry_in_the_home_has_just_its_name_as_its_path()
    {
        Assert.Equal("a.txt", FileEntryMapper.PathOf(string.Empty, "a.txt"));
    }

    /// <summary>A link is a file carrying its own flag, because the listing never follows it.</summary>
    [Fact]
    public void A_symlink_is_a_file_with_the_link_flag()
    {
        var row = FileEntryMapper.From(
            new AgentFileEntry("current", AgentFileEntryKind.Symlink, 7, 511, Modified, 1000, 1000, 1), string.Empty, "File");

        Assert.Equal(FileEntryKind.File, row.Kind);
        Assert.True(row.IsSymlink);
    }
}
