namespace Maran.Modules.Notifications.Options;

/// <summary>
/// The one deployment switch that lets the panel's outgoing-mail settings point at port 25 of the machine the
/// panel itself runs on. Bound from the <c>Notifications:Smtp</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Off by default, and that is the safe answer.</b> The mail server Maran manages refuses every local
/// client on port 25 (issue #531: that port is where OTHER servers deliver, and a process on the machine
/// must not hand it mail as if it were one), so a setting that names this machine on port 25 would fail
/// at the first message with a 554. The form refuses it early and points at the submission port.
/// </para>
/// <para>
/// <b>Opt in only when something else listens there.</b> A host that does not run Maran's mail stack
/// (mail not enabled) can still run a relay of its own on <c>localhost:25</c>, such as a null client that
/// forwards to a smarthost. Setting <see cref="AllowLocalPort25Relay"/> to <c>true</c>
/// (<c>Notifications__Smtp__AllowLocalPort25Relay=true</c> in the environment) removes the early refusal
/// and nothing else: if Maran's mail stack IS enabled on that host, its own refusal still applies.
/// </para>
/// </remarks>
public sealed class SmtpLocalRelayOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Notifications:Smtp";

    /// <summary>Whether the settings form may name this machine on port 25. Default <c>false</c>.</summary>
    public bool AllowLocalPort25Relay { get; set; }
}
