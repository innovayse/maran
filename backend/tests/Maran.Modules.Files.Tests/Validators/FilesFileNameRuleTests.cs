using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Tests.Validators;

/// <summary>Holds <see cref="FilesFileNameRule"/> to one name and nothing more.</summary>
public sealed class FilesFileNameRuleTests
{
    /// <summary>A plain name is accepted.</summary>
    [Fact]
    public void A_plain_name_is_valid()
    {
        Assert.True(FilesFileNameRule.IsValid("report final.txt"));
    }

    /// <summary>Anything that is more than one segment, or no usable segment, is refused.</summary>
    /// <param name="name">A name that must be refused.</param>
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("/a")]
    [InlineData("a\\b")]
    [InlineData("a\0")]
    [InlineData(".maran-trash")]
    public void A_name_that_is_not_one_clean_segment_is_refused(string name)
    {
        Assert.False(FilesFileNameRule.IsValid(name));
    }

    /// <summary>No name at all is refused.</summary>
    [Fact]
    public void A_missing_name_is_refused()
    {
        Assert.False(FilesFileNameRule.IsValid(null));
    }
}
