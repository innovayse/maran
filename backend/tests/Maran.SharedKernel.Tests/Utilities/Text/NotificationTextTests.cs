using Maran.SharedKernel.Utilities.Text;

namespace Maran.SharedKernel.Tests.Utilities.Text;

/// <summary>What <see cref="NotificationText"/> keeps of text bound for the feed and the webhook.</summary>
public sealed class NotificationTextTests
{
    /// <summary>Control characters, including the unit separator the feed joins arguments with, are removed.</summary>
    [Theory]
    [InlineData("a\u001fb", "ab")]
    [InlineData("line1\r\nline2", "line1line2")]
    [InlineData("tab\there", "tabhere")]
    public void Control_characters_are_removed(string input, string expected)
    {
        Assert.Equal(expected, NotificationText.Clean(input, 200));
    }

    /// <summary>Ordinary text, including non-Latin text, passes through untouched (the accepting control).</summary>
    [Theory]
    [InlineData("disk / is 91% full")]
    [InlineData("Արխիվը ձախողվեց")]
    [InlineData("Диск заполнен")]
    public void Ordinary_text_is_kept(string input)
    {
        Assert.Equal(input, NotificationText.Clean(input, 200));
    }

    /// <summary>Edges are trimmed and null reads as empty.</summary>
    [Fact]
    public void Edges_are_trimmed_and_null_is_empty()
    {
        Assert.Equal("x", NotificationText.Clean("  x \n", 200));
        Assert.Equal(string.Empty, NotificationText.Clean(null, 200));
    }

    /// <summary>The cap counts code points and never cuts a surrogate pair in half.</summary>
    [Fact]
    public void The_cap_counts_code_points_and_keeps_surrogate_pairs_whole()
    {
        var result = NotificationText.Clean("ab\U0001F600cd", 3);

        Assert.Equal("ab\U0001F600", result);
    }
}
