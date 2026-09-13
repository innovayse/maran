using System.Text;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Tests.Validators;

/// <summary>What this panel accepts as one cron command line, mirroring the agent's own rule.</summary>
public sealed class CronCommandRuleTests
{
    /// <summary>An ordinary command line is accepted.</summary>
    [Fact]
    public void An_ordinary_command_line_is_accepted()
    {
        Assert.True(CronCommandRule.IsOneCommandLine("/usr/bin/php /home/alice/site/cron.php"));
    }

    /// <summary>A percent sign and a hash are ordinary text in a command.</summary>
    [Theory]
    [InlineData("/usr/bin/date +%s > /tmp/stamp")]
    [InlineData("echo hi # a comment")]
    public void A_percent_sign_and_a_hash_are_ordinary_text_in_a_command(string command)
    {
        // Both were refused by an earlier design that put the command ON the crontab line, and both
        // broke real commands. The command lives in a file of its own now: `%` is only rewritten by
        // cron on a crontab LINE, and `#` only starts a comment there. Refusing them here would
        // refuse working jobs for a danger that no longer exists at this position.
        Assert.True(CronCommandRule.IsOneCommandLine(command));
    }

    /// <summary>An empty command is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_command_is_refused(string? command)
    {
        // An entry whose command is nothing is an entry that exists to do nothing, and cron still
        // wakes for it every time its schedule fires.
        Assert.False(CronCommandRule.IsOneCommandLine(command));
    }

    /// <summary>A control character is refused because the file holds exactly one line.</summary>
    [Theory]
    [InlineData("echo one\necho two")]
    [InlineData("echo one\r")]
    [InlineData("echo\tone")]
    [InlineData("echo\0one")]
    public void A_control_character_is_refused_because_the_file_holds_exactly_one_line(string command)
    {
        Assert.False(CronCommandRule.IsOneCommandLine(command));
    }

    /// <summary>Surrounding whitespace is refused rather than trimmed.</summary>
    [Theory]
    [InlineData(" ls")]
    [InlineData("ls ")]
    public void Surrounding_whitespace_is_refused_rather_than_trimmed(string command)
    {
        // The command is stored verbatim and compared verbatim when the agent decides whether an
        // entry duplicates one already installed, so ` ls` and `ls ` must not become two spellings
        // of one command — and trimming silently would show a customer something they did not type.
        Assert.False(CronCommandRule.IsOneCommandLine(command));
    }

    /// <summary>The length ceiling is measured in bytes and not in characters.</summary>
    [Fact]
    public void The_length_ceiling_is_measured_in_bytes_and_not_in_characters()
    {
        // The agent measures the bytes it writes. A command counted as characters would pass here at
        // four times the agent's limit and be refused AFTER the customer had been told their entry
        // was installed — so this is the assertion that keeps the two layers agreeing.
        var multiByte = new string('é', CronCommandRule.MaximumLengthInBytes / 2);
        Assert.Equal(CronCommandRule.MaximumLengthInBytes, Encoding.UTF8.GetByteCount(multiByte));
        Assert.True(CronCommandRule.IsOneCommandLine(multiByte));

        var oneOver = multiByte + "é";
        Assert.True(oneOver.Length < CronCommandRule.MaximumLengthInBytes);
        Assert.False(CronCommandRule.IsOneCommandLine(oneOver));
    }

    /// <summary>A command exactly at the ceiling is accepted.</summary>
    [Fact]
    public void A_command_exactly_at_the_ceiling_is_accepted()
    {
        Assert.True(CronCommandRule.IsOneCommandLine(new string('a', CronCommandRule.MaximumLengthInBytes)));
        Assert.False(CronCommandRule.IsOneCommandLine(new string('a', CronCommandRule.MaximumLengthInBytes + 1)));
    }
}
