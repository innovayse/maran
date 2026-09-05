using System.Text;

namespace Maran.Modules.Cron.Validators;

/// <summary>
/// What this panel accepts as one cron command line. Shared by the create and the update validators
/// so the two cannot drift into accepting different commands for the same entry.
/// </summary>
/// <remarks>
/// <para>
/// A mirror of the agent's own <c>CronCommand</c>, and only a mirror: the agent re-validates
/// everything and its answer is the one that decides what lands on the host (rules/architecture.md
/// "Agent"). Checking here is what lets a customer be told what is wrong with the line they typed
/// instead of being handed a refusal from a process they cannot see.
/// </para>
/// <para>
/// <b>The alphabet is a short list of refusals rather than a permitted set, and that is deliberate.</b>
/// The command never reaches the crontab: the agent writes it to a per-entry file under the
/// account's home and the installed crontab line runs that file. So the two characters a crontab
/// line genuinely cannot carry are ordinary text here — <c>%</c>, which cron rewrites into a newline
/// on a LINE it is not on, and <c>#</c>, which starts a comment in a crontab and does not in a shell
/// script. Refusing them would refuse working commands (<c>date +%s</c>, a trailing comment) for a
/// danger that does not exist at this position.
/// </para>
/// <para>
/// What remains refused is what a FILE cannot carry either: control characters, because the file
/// holds exactly one line, and an unbounded length.
/// </para>
/// </remarks>
public static class CronCommandRule
{
    /// <summary>The most bytes a command may be, matching the agent's own ceiling.</summary>
    /// <remarks>
    /// Measured in UTF-8 BYTES, not in .NET characters, because the agent measures the bytes it
    /// writes. A command of 3,000 emoji is 3,000 characters and 12,000 bytes: counted as characters
    /// it would pass here and be refused by the agent after the customer had been told their entry
    /// was accepted.
    /// </remarks>
    public const int MaximumLengthInBytes = 4096;

    /// <summary>Whether a candidate is one acceptable cron command line.</summary>
    /// <param name="candidate">The command line as the customer typed it.</param>
    /// <returns>
    /// True when it is a non-empty, bounded, single line with no control character and no leading or
    /// trailing whitespace.
    /// </returns>
    /// <remarks>
    /// Surrounding whitespace is refused rather than trimmed. The command is stored verbatim and
    /// compared verbatim when the agent decides whether an entry duplicates one already installed,
    /// so <c> ls</c> and <c>ls </c> must not become two spellings of one command — and trimming
    /// silently would show the customer something other than what they typed.
    /// </remarks>
    public static bool IsOneCommandLine(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(candidate) > MaximumLengthInBytes)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return !char.IsWhiteSpace(candidate[0]) && !char.IsWhiteSpace(candidate[^1]);
    }
}
