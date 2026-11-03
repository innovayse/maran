namespace Maran.Modules.Cron.Domain.Policies;

/// <summary>What of a run's output the panel can store (issue #335).</summary>
/// <remarks>
/// The output is whatever bytes the customer's program wrote, and a program may write a NUL. PostgreSQL's
/// text type cannot hold U+0000 at all — the insert fails — so a row carrying it is never written, and
/// before this policy that took the run's history with it. The character is removed, not escaped: it
/// has no printable form, the rest of the output is returned unchanged, and the run's end is
/// recorded from its status, not from its text.
/// </remarks>
public static class CronRunOutputPolicy
{
    /// <summary>The one character a text column cannot store.</summary>
    private const char Nul = '\0';

    /// <summary>The output as it is stored.</summary>
    /// <param name="output">What the program wrote, as the agent returned it.</param>
    /// <returns><paramref name="output"/> without any NUL character.</returns>
    public static string Storable(string output)
    {
        return output.Contains(Nul) ? output.Replace(Nul.ToString(), string.Empty, StringComparison.Ordinal) : output;
    }
}
