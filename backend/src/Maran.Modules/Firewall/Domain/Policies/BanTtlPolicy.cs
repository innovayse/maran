namespace Maran.Modules.Firewall.Domain.Policies;

/// <summary>
/// How long an automatic ban lasts, given how often the same address has already been banned today
/// (spec §15).
/// </summary>
/// <remarks>
/// <para>
/// <b>The ladder escalates, and that is the mechanism rather than a preference.</b> A single fixed
/// duration has to be either short enough to forgive a person who mistyped their password — in which
/// case a script simply waits it out and resumes — or long enough to stop the script, in which case
/// the first honest mistake costs a customer a day. Escalating separates the two by their own
/// behaviour: somebody who fat-fingered a password once is back in fifteen minutes, and something
/// that comes back a third time inside a day is held for a day.
/// </para>
/// <para>
/// The window the count is taken over is the caller's decision, not this type's; what belongs here
/// is only the rung-to-duration mapping, so that changing a duration is one edit in one file with
/// its own test.
/// </para>
/// </remarks>
public static class BanTtlPolicy
{
    /// <summary>How long the first ban of an address inside the window lasts.</summary>
    public static readonly TimeSpan FirstOffence = TimeSpan.FromMinutes(15);

    /// <summary>How long the second one lasts.</summary>
    public static readonly TimeSpan SecondOffence = TimeSpan.FromHours(1);

    /// <summary>How long the third and every later one lasts.</summary>
    public static readonly TimeSpan RepeatOffence = TimeSpan.FromHours(24);

    /// <summary>The duration to ban for, given how many episodes the address already has in the window.</summary>
    /// <param name="priorEpisodes">
    /// How many times this address has already been banned inside the counting window. Zero for an
    /// address being banned for the first time.
    /// </param>
    /// <returns>
    /// <see cref="FirstOffence"/>, <see cref="SecondOffence"/> or <see cref="RepeatOffence"/>.
    /// </returns>
    public static TimeSpan ForPriorEpisodes(int priorEpisodes)
    {
        return priorEpisodes switch
        {
            <= 0 => FirstOffence,
            1 => SecondOffence,
            _ => RepeatOffence,
        };
    }
}
