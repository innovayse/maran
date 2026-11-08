using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Tests.Validators;

/// <summary>Holds <see cref="FilesPathListRule"/> to applying the single-path rule to every item.</summary>
public sealed class FilesPathListRuleTests
{
    /// <summary>A batch of legal entry paths is accepted.</summary>
    [Fact]
    public void A_batch_of_legal_entry_paths_is_accepted()
    {
        Assert.True(FilesPathListRule.IsEveryEntryPath(["a", "b/c"]));
    }

    /// <summary>One hostile item spoils the batch.</summary>
    [Fact]
    public void One_hostile_item_refuses_the_batch()
    {
        Assert.False(FilesPathListRule.IsEveryEntryPath(["a", "../b"]));
        Assert.False(FilesPathListRule.IsEveryEntryPath(["a", string.Empty]));
        Assert.False(FilesPathListRule.IsEveryEntryPath(null));
    }

    /// <summary>One item in the trash directory is found, and a clean batch has none.</summary>
    [Fact]
    public void A_batch_naming_the_trash_directory_is_not_none_reserved()
    {
        Assert.False(FilesPathListRule.IsNoneReserved(["a", ".maran-trash/x"]));
        Assert.True(FilesPathListRule.IsNoneReserved(["a", "b"]));
    }
}
