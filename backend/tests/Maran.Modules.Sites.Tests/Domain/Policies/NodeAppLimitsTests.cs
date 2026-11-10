using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="NodeAppLimits"/> and <see cref="NodeVersionFormat"/>.</summary>
public sealed class NodeAppLimitsTests
{
    /// <summary>A start command is one printable line within the length cap.</summary>
    /// <param name="command">The submitted command.</param>
    /// <param name="expected">Whether it is accepted.</param>
    [Theory]
    [InlineData("npm start", true)]
    [InlineData("node --max-old-space-size=512 dist/main.js", true)]
    [InlineData("", false)]
    [InlineData(" \t ", false)]
    [InlineData("npm start\n", false)]
    [InlineData("npm\tstart", false)]
    [InlineData("npm start\u0007", false)]
    public void A_start_command_is_one_printable_line(string command, bool expected)
    {
        Assert.Equal(expected, NodeAppLimits.IsStartCommand(command));
    }

    /// <summary>The length cap is inclusive: the longest command passes and one more does not.</summary>
    [Fact]
    public void The_length_cap_is_inclusive()
    {
        Assert.True(NodeAppLimits.IsStartCommand(new string('a', NodeAppLimits.MaximumStartCommandLength)));
        Assert.False(NodeAppLimits.IsStartCommand(new string('a', NodeAppLimits.MaximumStartCommandLength + 1)));
    }

    /// <summary>A null command is refused rather than thrown on.</summary>
    [Fact]
    public void A_null_command_is_refused()
    {
        Assert.False(NodeAppLimits.IsStartCommand(null!));
    }

    /// <summary>A Node major is one or two digits and nothing else, a trailing newline included.</summary>
    /// <param name="version">The submitted major.</param>
    /// <param name="expected">Whether it matches.</param>
    [Theory]
    [InlineData("8", true)]
    [InlineData("22", true)]
    [InlineData("", false)]
    [InlineData("222", false)]
    [InlineData("22.1", false)]
    [InlineData("22\n", false)]
    [InlineData("v22", false)]
    public void A_node_major_is_one_or_two_digits(string version, bool expected)
    {
        Assert.Equal(expected, System.Text.RegularExpressions.Regex.IsMatch(version, NodeVersionFormat.Pattern));
    }
}
