using Maran.Modules.Files.Domain.Policies;

namespace Maran.Modules.Files.Tests.Domain.Policies;

/// <summary>Holds <see cref="FilesModePolicy"/> to octal digits, nine permission bits and nothing else.</summary>
public sealed class FilesModePolicyTests
{
    /// <summary>Octal digits are read as permission bits.</summary>
    [Theory]
    [InlineData("0", 0u)]
    [InlineData("644", 420u)]
    [InlineData("755", 493u)]
    [InlineData("777", 511u)]
    [InlineData("7", 7u)]
    public void Octal_digits_are_read_as_permission_bits(string text, uint expected)
    {
        Assert.True(FilesModePolicy.TryParse(text, out var mode));
        Assert.Equal(expected, mode);
    }

    /// <summary>Anything but one to three octal digits is refused, special bits included.</summary>
    /// <param name="text">A refused mode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("-1")]
    [InlineData("1777")]
    [InlineData("4755")]
    [InlineData("rwx")]
    [InlineData("6 4")]
    public void Anything_but_one_to_three_octal_digits_is_refused(string text)
    {
        Assert.False(FilesModePolicy.TryParse(text, out _));
    }

    /// <summary>No mode at all is refused.</summary>
    [Fact]
    public void A_missing_mode_is_refused()
    {
        Assert.False(FilesModePolicy.TryParse(null, out _));
    }

    /// <summary>A mode is written as three octal digits and bits above the permission bits are dropped.</summary>
    [Fact]
    public void A_mode_is_written_as_three_octal_digits_without_special_bits()
    {
        Assert.Equal("644", FilesModePolicy.Format(420));
        Assert.Equal("007", FilesModePolicy.Format(7));
        Assert.Equal("755", FilesModePolicy.Format(Convert.ToUInt32("4755", 8)));
    }

    /// <summary>Folders get execute wherever read is set, and nowhere else.</summary>
    /// <param name="fileText">The file mode.</param>
    /// <param name="expectedText">The folders' mode.</param>
    [Theory]
    [InlineData("644", "755")]
    [InlineData("600", "700")]
    [InlineData("640", "750")]
    [InlineData("755", "755")]
    [InlineData("222", "222")]
    [InlineData("000", "000")]
    [InlineData("604", "705")]
    public void Folders_get_execute_wherever_read_is_set_and_nowhere_else(string fileText, string expectedText)
    {
        Assert.True(FilesModePolicy.TryParse(fileText, out var file));

        Assert.Equal(expectedText, FilesModePolicy.Format(FilesModePolicy.DirectoryModeFor(file)));
    }

    /// <summary>A folder mode is read like any mode, except that zero is refused.</summary>
    [Fact]
    public void A_folder_mode_is_read_like_a_mode_except_that_zero_is_refused()
    {
        Assert.True(FilesModePolicy.TryParseFolderMode("750", out var mode));
        Assert.Equal(0b111_101_000u, mode);
        Assert.False(FilesModePolicy.TryParseFolderMode("000", out _));
        Assert.False(FilesModePolicy.TryParseFolderMode("0", out _));
        Assert.False(FilesModePolicy.TryParseFolderMode("8", out _));
        Assert.False(FilesModePolicy.TryParseFolderMode(null, out _));
    }
}
