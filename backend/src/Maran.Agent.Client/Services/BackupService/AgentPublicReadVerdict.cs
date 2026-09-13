namespace Maran.Agent.Client.Services.BackupService;

/// <summary>What an anonymous fetch of a destination's own object established.</summary>
/// <remarks>
/// Three states and not a boolean, because "the fetch could not be made" is not "the object is
/// private" and a boolean has nowhere to put it: it would report a bucket as safe on the strength of
/// a request that was never answered. Only <see cref="Private"/> may save a destination.
///
/// <para>
/// Hand-written rather than the generated wire enum, because a generated type may not leave the
/// invoker layer (<c>WireTypeContainmentTests</c>).
/// </para>
/// </remarks>
public enum AgentPublicReadVerdict
{
    /// <summary>
    /// The agent stated no verdict — the wire's zero value, or a verdict this panel build has no
    /// member for. Nothing was established, so it may not save a destination either.
    /// </summary>
    Unspecified = 0,

    /// <summary>The fetch was made and the destination refused it.</summary>
    Private = 1,

    /// <summary>The fetch was made and the object came back: the destination serves backups to anyone.</summary>
    PubliclyReadable = 2,

    /// <summary>The fetch could not be made or was not answered. Nothing was established.</summary>
    Unproven = 3,
}
