using Maran.Modules.Ftp.Domain.Policies;

namespace Maran.Modules.Ftp.Domain.Entities;

/// <summary>
/// What this installation's FTPS daemon was last configured with, and whether the panel currently
/// wants it running. Exactly one row exists on a server, addressed by <see cref="SingletonId"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This row is an INTENTION, never an observation.</b> It records what the panel asked the host
/// for; what the daemon is actually doing is read back from the agent on every status call and is
/// never stored here. The distinction is the whole reason the contract's status message exists: a
/// screen assembled from this row would agree with the panel by construction and report a healthy
/// daemon it had never looked at.
/// </para>
/// <para>
/// <b>It is not tenant-scoped and carries no <c>AccountId</c>, and that is correct rather than an
/// omission.</b> There is one FTPS daemon on the server, switched on by an administrator for the
/// whole installation; a per-account copy of this row would be a per-account copy of one host-wide
/// fact. Every endpoint that reaches it is <c>AdminOnly</c>, so the authorisation is the policy on
/// the controller rather than a query filter — and because the entity carries no <c>AccountId</c>,
/// <c>TenantScopeTests</c> does not ask it for one.
/// </para>
/// <para>
/// <b>The range and the ceiling are seeded from <see cref="FtpsDefaults"/> and never from a
/// caller.</b> They are stored rather than read from the constants at every use so that a server
/// whose defaults move in a later release keeps serving the range its firewall was opened for until
/// an administrator enables FTPS again — the two numbers have to stay the same two numbers, and a
/// constant that changes underneath a running host is how they stop being.
/// </para>
/// <para>
/// <see cref="Ipv4Only"/> is the one field written from what the HOST decided rather than from what
/// the panel asked: the enable path probes for an IPv6 listening bind and falls back, and the panel
/// remembers the mode so a screen can state it.
/// </para>
/// </remarks>
public sealed class FtpsSettings
{
    /// <summary>The identity of the single row, so a second one cannot be inserted.</summary>
    /// <remarks>
    /// A fixed key rather than a uniqueness rule over a discriminator column: the primary key is
    /// then what forbids a second row, which is a constraint the database already has rather than
    /// one somebody has to remember to add. Every read is <c>SingleOrDefault</c> against it.
    /// </remarks>
    public static readonly Guid SingletonId = new("6dd5f52b-2d55-4d9c-9c6b-6a2d0f4d0f01");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The hostname the daemon serves, and the name its certificate material is filed under.</summary>
    public string Hostname { get; private set; }

    /// <summary>Whether the panel currently wants the daemon running.</summary>
    /// <remarks>
    /// The panel's intention, not the daemon's state. A host whose unit has crashed reports
    /// <c>Enabled</c> here and not running there, and it is precisely that disagreement a status
    /// screen exists to show.
    /// </remarks>
    public bool Enabled { get; private set; }

    /// <summary>Lowest port of the passive data range the daemon was configured with, inclusive.</summary>
    public int PassivePortMin { get; private set; }

    /// <summary>Highest port of the passive data range the daemon was configured with, inclusive.</summary>
    public int PassivePortMax { get; private set; }

    /// <summary>
    /// The address advertised in the PASV reply for a host behind NAT, or empty for the ordinary
    /// host where the daemon answers with the address the control connection arrived on.
    /// </summary>
    public string PassiveAddress { get; private set; }

    /// <summary>The daemon's concurrent-session ceiling.</summary>
    public int MaxClients { get; private set; }

    /// <summary>The live configuration is the IPv4-only fallback, as the host reported after enabling.</summary>
    public bool Ipv4Only { get; private set; }

    /// <summary>When this row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the first enable of FTPS on this installation.</summary>
    /// <param name="hostname">The hostname the daemon serves.</param>
    /// <param name="passiveAddress">The PASV address to advertise, or empty for none.</param>
    /// <param name="ipv4Only">The listen mode the host chose, as the enable response reported it.</param>
    /// <param name="at">The instant of the change, taken from <see cref="IClock"/>.</param>
    public FtpsSettings(string hostname, string passiveAddress, bool ipv4Only, DateTimeOffset at)
    {
        Id = SingletonId;
        Hostname = hostname;
        PassiveAddress = passiveAddress;
        Enabled = true;
        Ipv4Only = ipv4Only;
        PassivePortMin = FtpsDefaults.PassivePortMin;
        PassivePortMax = FtpsDefaults.PassivePortMax;
        MaxClients = FtpsDefaults.MaxClients;
        UpdatedAt = at;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private FtpsSettings()
    {
        Hostname = string.Empty;
        PassiveAddress = string.Empty;
    }

    /// <summary>Records that FTPS is on, for this hostname, in the mode the host reported.</summary>
    /// <param name="hostname">The hostname the daemon now serves.</param>
    /// <param name="passiveAddress">The PASV address to advertise, or empty for none.</param>
    /// <param name="ipv4Only">The listen mode the host chose, as the enable response reported it.</param>
    /// <param name="at">The instant of the change.</param>
    /// <remarks>
    /// The range and the ceiling are deliberately NOT rewritten from the constants here. A host that
    /// was enabled once is already serving a range an operator opened in their firewall, and moving
    /// it under them because a later release changed a default would break every passive transfer on
    /// the server with nothing on any screen to explain it.
    /// </remarks>
    public void Enable(string hostname, string passiveAddress, bool ipv4Only, DateTimeOffset at)
    {
        Hostname = hostname;
        PassiveAddress = passiveAddress;
        Ipv4Only = ipv4Only;
        Enabled = true;
        UpdatedAt = at;
    }

    /// <summary>Records that the panel no longer wants the daemon running.</summary>
    /// <param name="at">The instant of the change.</param>
    /// <remarks>
    /// The hostname and the range survive a disable, because disabling a service is not forgetting
    /// how it was configured: an operator who switches FTPS back on expects the same hostname and
    /// the same ports, and re-typing them is how the panel and the firewall stop agreeing.
    /// </remarks>
    public void Disable(DateTimeOffset at)
    {
        Enabled = false;
        UpdatedAt = at;
    }
}
