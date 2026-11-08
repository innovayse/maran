namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// How many days of rotated log archives one site keeps: the range a customer may choose from, and
/// the number every site starts with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maximum is not a choice of this module.</b> It is the installer's, <c>rotate 14</c> in
/// <c>installer/logrotate/maran-sites</c>, which rotates every site's log under one shared stanza and
/// is the hard bound on how much a host keeps. A site's own setting can only SHORTEN that: the panel
/// asks the agent to remove archives beyond the site's number, and never to keep more than the
/// stanza does. A bug here therefore costs disk by at most the fourteen files the installer already
/// allows, which is the direction that fails closed. Raising the ceiling is a decision about the
/// host's disk and is made in the installer, with this constant following it;
/// <c>SiteLogRetentionTests</c> reads that file and fails when the two disagree.
/// </para>
/// <para>
/// <b>The default is the maximum</b>, so no site loses an archive it has today until its owner asks.
/// </para>
/// </remarks>
public static class SiteLogRetention
{
    /// <summary>The fewest days a site may keep.</summary>
    public const int MinimumDays = 1;

    /// <summary>The most days a site may keep: the installer's <c>rotate 14</c>.</summary>
    public const int MaximumDays = 14;

    /// <summary>What a site keeps until its owner chooses otherwise.</summary>
    public const int DefaultDays = MaximumDays;

    /// <summary>Whether <paramref name="days"/> is a number of days a site may keep.</summary>
    /// <param name="days">The candidate.</param>
    /// <returns><c>true</c> from <see cref="MinimumDays"/> to <see cref="MaximumDays"/> inclusive.</returns>
    public static bool IsAllowed(int days)
    {
        return days >= MinimumDays && days <= MaximumDays;
    }
}
