using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Identity.Options;

/// <summary>
/// When repeated failed sign-ins from one address stop being clumsiness and start being an attack.
/// Bound from the <c>BruteForce</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configurable, with the spec's numbers as defaults.</b> The design ruling fixes twenty-five
/// failures inside ten minutes, counted across any usernames, and says the pair is policy. Neither
/// number is a constant in the detector for that reason: an operator whose panel is behind a NAT
/// that puts a whole office on one address needs a higher count, and one running a panel nobody but
/// they sign into can afford a much lower one. Both are settings, and both are validated at startup
/// so a nonsensical value refuses the boot instead of quietly disabling the protection
/// (rules/csharp.md "Options validated at startup").
/// </para>
/// <para>
/// <b>Neither value is a secret</b>, so both belong in <c>appsettings.json</c> or an operator's
/// <c>panel.env</c> rather than anywhere protected — the defaults below are what a panel that
/// configures neither one runs with.
/// </para>
/// <para>
/// <b>The count is per SOURCE ADDRESS and across every username.</b> That is the whole point of
/// counting here rather than on the user row: the per-user lockout already stops guessing at ONE
/// account, and is blind to a script that tries one password against a thousand names. This counter
/// is blind to nothing an address does.
/// </para>
/// </remarks>
public sealed class BruteForceOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "BruteForce";

    /// <summary>
    /// How many refused sign-ins from one address inside <see cref="WindowMinutes"/> are an attack.
    /// </summary>
    /// <remarks>
    /// Twenty-five, from the design ruling. The upper bound of the range is generous because a
    /// large shared-address deployment is a legitimate reason to raise it; there is no upper bound
    /// at which the setting becomes dangerous, only one at which it becomes useless.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxFailuresPerAddress { get; set; } = 25;

    /// <summary>How long the counting window is, in minutes.</summary>
    /// <remarks>
    /// Ten, from the design ruling. A window is what separates an attack from a bad week: without
    /// one, twenty-five mistyped passwords spread over a year would eventually ban a customer.
    /// </remarks>
    [Range(1, 1_440)]
    public int WindowMinutes { get; set; } = 10;

    /// <summary><see cref="WindowMinutes"/> as the interval the detector actually measures with.</summary>
    public TimeSpan Window
    {
        get
        {
            return TimeSpan.FromMinutes(WindowMinutes);
        }
    }
}
