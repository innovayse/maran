using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Tests.Validators;

/// <summary>Holds <see cref="FilesPathRule"/> to what the panel accepts as a path inside a home.</summary>
public sealed class FilesPathRuleTests
{
    /// <summary>Paths that must be accepted: the positive control for the refusals below.</summary>
    public static TheoryData<string> Legal
    {
        get
        {
            return new TheoryData<string>
            {
                string.Empty,
                "public_html",
                "public_html/index.php",
                "a/b/c.tar.gz",
                ".htaccess",
                "..hidden",
                "name with spaces/файл.txt",
                "%2e%2e",
                "a∕b",
            };
        }
    }

    /// <summary>Paths that must be refused: traversal, absolute, hostile characters and bounds.</summary>
    public static TheoryData<string> Hostile
    {
        get
        {
            return new TheoryData<string>
            {
                "..",
                ".",
                "a/../b",
                "a/../../b",
                "../etc/passwd",
                "/etc/passwd",
                "a\\..\\b",
                "a/b/",
                "a//b",
                "a\0b",
                "a\nb",
                "a\u2028b",
                "a/./b",
                new string('x', 256),
                string.Join('/', Enumerable.Repeat("a", 65)),
                string.Join('/', Enumerable.Repeat(new string('y', 200), 21)),
            };
        }
    }

    /// <summary>A legal path is accepted, so the refusals are not a rule that refuses everything.</summary>
    /// <param name="path">A legal path.</param>
    [Theory]
    [MemberData(nameof(Legal))]
    public void A_legal_path_is_well_formed(string path)
    {
        Assert.True(FilesPathRule.IsWellFormed(path));
    }

    /// <summary>A hostile path is refused.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(Hostile))]
    public void A_hostile_path_is_not_well_formed(string path)
    {
        Assert.False(FilesPathRule.IsWellFormed(path));
    }

    /// <summary>A lone surrogate has no UTF-8 form and is refused, while a complete pair is accepted.</summary>
    /// <remarks>
    /// Not a row of <see cref="Hostile"/>: xUnit carries theory arguments between discovery and execution as
    /// text, which turns a lone surrogate into U+FFFD, a legal character, so the row would test a different
    /// path from the one it names. The pair is the control that stops a rule refusing every surrogate from passing.
    /// </remarks>
    [Fact]
    public void A_lone_surrogate_is_refused_and_a_complete_pair_is_accepted()
    {
        Assert.False(FilesPathRule.IsWellFormed("a\ud800b"));
        Assert.False(FilesPathRule.IsWellFormed("a\udc00b"));
        Assert.False(FilesPathRule.IsWellFormed("a\ud800"));
        Assert.True(FilesPathRule.IsWellFormed("a\ud83d\ude00b"));
    }

    /// <summary>No path at all is refused.</summary>
    [Fact]
    public void A_missing_path_is_not_well_formed()
    {
        Assert.False(FilesPathRule.IsWellFormed(null));
    }

    /// <summary>The home is a path but names no entry.</summary>
    [Fact]
    public void The_home_is_well_formed_but_is_not_an_entry_path()
    {
        Assert.True(FilesPathRule.IsWellFormed(string.Empty));
        Assert.False(FilesPathRule.IsEntryPath(string.Empty));
        Assert.True(FilesPathRule.IsEntryPath("a"));
    }

    /// <summary>The length bound counts UTF-8 bytes, as the agent does, not characters.</summary>
    [Fact]
    public void A_segment_is_measured_in_bytes_not_characters()
    {
        var oneHundredTwentyEightTwoByteLetters = new string('ы', 128);

        Assert.False(FilesPathRule.IsWellFormed(oneHundredTwentyEightTwoByteLetters));
        Assert.True(FilesPathRule.IsWellFormed(new string('ы', 127)));
    }

    /// <summary>Only the first segment being the trash name makes a path reserved.</summary>
    [Theory]
    [InlineData(".maran-trash", true)]
    [InlineData(".maran-trash/entries/x", true)]
    [InlineData("a/.maran-trash", false)]
    [InlineData(".maran-trash2", false)]
    [InlineData("", false)]
    public void A_path_is_reserved_only_when_its_first_segment_is_the_trash(string path, bool reserved)
    {
        Assert.Equal(reserved, FilesPathRule.IsReserved(path));
    }
}
