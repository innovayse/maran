using System.Text;
using Maran.Modules.Updates.Domain.Policies;

namespace Maran.Modules.Updates.Tests.Domain.Policies;

/// <summary>What the panel makes of a state file's text, which a hostile or damaged file must never turn into a surprise.</summary>
public sealed class StateFileTextTests
{
    /// <summary>Ordinary KEY=value lines are read, and a value may itself contain an equals sign.</summary>
    [Fact]
    public void Ordinary_assignments_are_read_and_a_value_may_contain_an_equals_sign()
    {
        var values = StateFileText.Parse("CHANNEL=stable\nVERSION=1.2.3\nNOTES_URL=https://example.net/?a=b\n");

        Assert.Equal("stable", values["CHANNEL"]);
        Assert.Equal("1.2.3", values["VERSION"]);
        Assert.Equal("https://example.net/?a=b", values["NOTES_URL"]);
    }

    /// <summary>The first assignment of a key wins, so a later line cannot override what root wrote first.</summary>
    [Fact]
    public void The_first_assignment_of_a_key_wins()
    {
        var values = StateFileText.Parse("VERSION=1.0.0\nVERSION=9.9.9\n");

        Assert.Equal("1.0.0", values["VERSION"]);
    }

    /// <summary>Lines that are not an upper-case key and an equals sign are ignored, whatever else the file holds.</summary>
    [Theory]
    [InlineData("=novalue")]
    [InlineData("lowercase=1")]
    [InlineData("KEY WITH SPACE=1")]
    [InlineData("no equals sign")]
    [InlineData("# COMMENT=1")]
    [InlineData("0KEY=1")]
    public void A_line_that_is_not_a_plain_assignment_is_ignored(string line)
    {
        Assert.Empty(StateFileText.Parse(line + "\n"));
    }

    /// <summary>A file over the size limit is refused whole, not truncated, and a file just under it is read.</summary>
    [Fact]
    public void A_file_over_the_limit_is_refused_whole_and_one_under_it_is_read()
    {
        var padding = new string('x', StateFileText.MaxBytes - "A=".Length);

        Assert.Equal(padding, StateFileText.Parse("A=" + padding)["A"]);
        Assert.Empty(StateFileText.Parse("A=" + padding + "y"));
        Assert.Equal(StateFileText.MaxBytes + 1, Encoding.UTF8.GetByteCount("A=" + padding + "y"));
    }

    /// <summary>A file the reader could not read is no assignments at all.</summary>
    [Fact]
    public void A_null_file_is_no_assignments()
    {
        Assert.Empty(StateFileText.Parse(null));
    }

    /// <summary>A Windows line ending does not leave a carriage return inside the value.</summary>
    [Fact]
    public void A_carriage_return_is_not_kept_in_a_value()
    {
        Assert.Equal("stable", StateFileText.Parse("CHANNEL=stable\r\n")["CHANNEL"]);
    }
}
