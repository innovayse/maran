using System.Text.RegularExpressions;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="SiteLogRetention"/>, and its agreement with the installer's rotation.</summary>
public sealed class SiteLogRetentionTests
{
    /// <summary>Only the numbers from one to fourteen are allowed.</summary>
    /// <param name="days">The candidate.</param>
    /// <param name="expected">Whether it is allowed.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(-3, false)]
    public void Only_the_numbers_from_one_to_fourteen_are_allowed(int days, bool expected)
    {
        Assert.Equal(expected, SiteLogRetention.IsAllowed(days));
    }

    /// <summary>A site keeps the most the installer's rotation does until its owner chooses less.</summary>
    [Fact]
    public void A_site_keeps_the_most_the_installers_rotation_does_until_its_owner_chooses_less()
    {
        Assert.Equal(SiteLogRetention.MaximumDays, SiteLogRetention.DefaultDays);
    }

    /// <summary>The ceiling equals the installer's <c>rotate</c> count, so a customer can only shorten what the host keeps.</summary>
    [Fact]
    public void The_ceiling_equals_the_installers_rotate_count()
    {
        var logrotate = File.ReadAllText(LogrotateFile());

        Assert.Equal(SiteLogRetention.MaximumDays, RotateCountOf(logrotate));
    }

    /// <summary>The comparison notices a rotate count that differs from the ceiling.</summary>
    /// <remarks>The positive control: a guard that compares against nothing passes however the two drift.</remarks>
    [Fact]
    public void The_comparison_notices_a_rotate_count_that_differs_from_the_ceiling()
    {
        Assert.NotEqual(SiteLogRetention.MaximumDays, RotateCountOf("/var/log/maran/sites/*/*.log {\n    rotate 15\n}\n"));
        Assert.Equal(SiteLogRetention.MaximumDays, RotateCountOf("/var/log/maran/sites/*/*.log {\n    rotate 14\n}\n"));
    }

    /// <summary>Reads the number after <c>rotate</c> in a logrotate stanza.</summary>
    /// <param name="logrotate">The file's text.</param>
    /// <returns>The count.</returns>
    private static int RotateCountOf(string logrotate)
    {
        var match = Regex.Match(logrotate, @"^\s*rotate\s+(\d+)\s*$", RegexOptions.Multiline);
        Assert.True(match.Success, "the logrotate file has no rotate directive");

        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Finds the installer's logrotate file by walking up to the repository root.</summary>
    /// <returns>The file's path.</returns>
    private static string LogrotateFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "installer", "logrotate", "maran-sites");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("installer/logrotate/maran-sites was not found above the test binary.");
    }
}
