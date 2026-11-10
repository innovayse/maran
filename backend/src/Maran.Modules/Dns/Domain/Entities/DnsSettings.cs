using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Domain.Entities;

/// <summary>
/// The installation's DNS settings: the name servers every zone names, the SOA mailbox, the addresses the
/// default records point at, and whether the administrator switched the name server on. At most one row.
/// </summary>
/// <remarks>
/// <para>
/// This row belongs to the server and to no customer, so it carries no account id and no tenant filter —
/// a filter over a row with no owner would invent an owner and read as protection while protecting nothing.
/// What authorises every path to it is the <c>AdminOnly</c> policy on <c>DnsServerController</c>.
/// </para>
/// <para>
/// <see cref="Enabled"/> is an INTENTION: what the administrator asked of the host. What the daemon is
/// doing is read back from the agent on every status call and never stored.
/// </para>
/// </remarks>
public sealed class DnsSettings
{
    /// <summary>The fixed identity of the one row, so a duplicate is a key violation and not a second opinion.</summary>
    public static readonly Guid SingletonId = new("3f6b2d9e-6a7c-4f0e-9a52-0d1b8c4e7a11");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The first name server every zone names and the SOA's primary; empty until stated.</summary>
    public string PrimaryNameserver { get; private set; }

    /// <summary>The second name server every zone names; empty when the server has only one.</summary>
    public string SecondaryNameserver { get; private set; }

    /// <summary>The SOA responsible mailbox in dotted form (<c>hostmaster.example.com</c> means <c>hostmaster@example.com</c>).</summary>
    public string HostmasterMailbox { get; private set; }

    /// <summary>The server's public IPv4 address, or empty. Default records point at it.</summary>
    public string PublicIpv4 { get; private set; }

    /// <summary>The server's public IPv6 address, or empty. Default records point at it.</summary>
    public string PublicIpv6 { get; private set; }

    /// <summary>The default time to live of a zone, in seconds.</summary>
    public int DefaultTtl { get; private set; }

    /// <summary>Whether the administrator asked for the name server to run.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// The zone template a zone created for a new site is made from, or null when none is chosen. Not a foreign
    /// key: a template that is the default cannot be deleted, so the row it names always exists.
    /// </summary>
    public Guid? DefaultTemplateId { get; private set; }

    /// <summary>The instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The name servers every zone names, primary first; the second only when stated.</summary>
    public IReadOnlyList<string> Nameservers
    {
        get { return SecondaryNameserver.Length > 0 ? [PrimaryNameserver, SecondaryNameserver] : [PrimaryNameserver]; }
    }

    /// <summary>
    /// True when the name servers and the SOA mailbox are stated, which is what a zone cannot be published
    /// without: every zone names them.
    /// </summary>
    public bool IsComplete
    {
        get { return PrimaryNameserver.Length > 0 && HostmasterMailbox.Length > 0; }
    }

    /// <summary>Creates the settings with nothing stated, the name server off and the default time to live.</summary>
    /// <param name="now">The creation instant, from the clock.</param>
    public DnsSettings(DateTimeOffset now)
    {
        Id = SingletonId;
        PrimaryNameserver = string.Empty;
        SecondaryNameserver = string.Empty;
        HostmasterMailbox = string.Empty;
        PublicIpv4 = string.Empty;
        PublicIpv6 = string.Empty;
        DefaultTtl = DnsLimits.DefaultTtl;
        Enabled = false;
        UpdatedAt = now;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DnsSettings()
    {
        PrimaryNameserver = string.Empty;
        SecondaryNameserver = string.Empty;
        HostmasterMailbox = string.Empty;
        PublicIpv4 = string.Empty;
        PublicIpv6 = string.Empty;
    }

    /// <summary>Replaces the stated settings; the on/off intention is untouched.</summary>
    /// <param name="primaryNameserver">The first name server, a host name.</param>
    /// <param name="secondaryNameserver">The second name server, or empty.</param>
    /// <param name="hostmasterMailbox">The SOA mailbox in dotted form.</param>
    /// <param name="publicIpv4">The public IPv4 address, or empty.</param>
    /// <param name="publicIpv6">The public IPv6 address, or empty.</param>
    /// <param name="defaultTtl">The default time to live, in seconds.</param>
    /// <param name="now">The change instant, from the clock.</param>
    public void Save(
        string primaryNameserver,
        string secondaryNameserver,
        string hostmasterMailbox,
        string publicIpv4,
        string publicIpv6,
        int defaultTtl,
        DateTimeOffset now)
    {
        PrimaryNameserver = primaryNameserver;
        SecondaryNameserver = secondaryNameserver;
        HostmasterMailbox = hostmasterMailbox;
        PublicIpv4 = publicIpv4;
        PublicIpv6 = publicIpv6;
        DefaultTtl = defaultTtl;
        UpdatedAt = now;
    }

    /// <summary>Chooses the zone template a zone created for a new site is made from.</summary>
    /// <param name="templateId">The template, which the caller has found to exist.</param>
    /// <param name="now">The change instant, from the clock.</param>
    public void ChooseDefaultTemplate(Guid templateId, DateTimeOffset now)
    {
        DefaultTemplateId = templateId;
        UpdatedAt = now;
    }

    /// <summary>Records that the administrator asked for the name server to run.</summary>
    /// <param name="now">The change instant, from the clock.</param>
    public void Enable(DateTimeOffset now)
    {
        Enabled = true;
        UpdatedAt = now;
    }

    /// <summary>Records that the administrator asked for the name server to stop.</summary>
    /// <param name="now">The change instant, from the clock.</param>
    public void Disable(DateTimeOffset now)
    {
        Enabled = false;
        UpdatedAt = now;
    }
}
