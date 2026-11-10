using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Persistence;

namespace Maran.Modules.Dns.Seeders;

/// <summary>
/// Puts the two zone templates a fresh installation ships with into the database, and chooses the first as the
/// one new sites get their zone from when the administrator has chosen none.
/// </summary>
/// <remarks>
/// <para>
/// <b>"web"</b> points the apex at the server (A and AAAA) and <c>www</c> at the apex. <b>"web and mail"</b> is
/// that plus an MX naming <c>mail.{apex}</c>, that host's addresses, a bare SPF policy and a monitoring-only DMARC
/// policy. The records state only the closed template variables: an IPv6 address the settings do not hold skips
/// the AAAA records at creation, and the panel invents none.
/// </para>
/// <para>
/// <b>Idempotent, and it never overwrites an administrator's edit.</b> A built-in template is written only when
/// no row has its fixed identity, so a template that was edited (they can be) keeps the edit, and a restart
/// inserts nothing twice. A row an administrator made under the same name first stops the built-in from being
/// written rather than being refused by the unique name at start-up. The default is chosen only while none is
/// chosen, so the administrator's later choice is never undone.
/// </para>
/// </remarks>
public sealed class ZoneTemplateSeeder
{
    /// <summary>The name of the built-in web template.</summary>
    public const string WebName = "web";

    /// <summary>The name of the built-in web and mail template.</summary>
    public const string WebAndMailName = "web and mail";

    /// <summary>The web template's stable identity, fixed so re-seeding is idempotent.</summary>
    public static readonly Guid WebTemplateId = Guid.Parse("33333333-0000-4000-8000-000000000001");

    /// <summary>The web and mail template's stable identity, fixed so re-seeding is idempotent.</summary>
    public static readonly Guid WebAndMailTemplateId = Guid.Parse("33333333-0000-4000-8000-000000000002");

    /// <summary>The records of the web template, in the order a zone is created with them.</summary>
    public static IReadOnlyList<TemplateRecordDraft> WebRecords { get; } =
    [
        new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}"),
        new TemplateRecordDraft("@", RecordType.Aaaa, null, "{ipv6}"),
        new TemplateRecordDraft("www", RecordType.Cname, null, "{apex}."),
    ];

    /// <summary>The records of the web and mail template: the web records, then the mail ones.</summary>
    public static IReadOnlyList<TemplateRecordDraft> WebAndMailRecords { get; } =
    [
        .. WebRecords,
        new TemplateRecordDraft("@", RecordType.Mx, null, "mail.{apex}", Priority: 10),
        new TemplateRecordDraft("mail", RecordType.A, null, "{ipv4}"),
        new TemplateRecordDraft("mail", RecordType.Aaaa, null, "{ipv6}"),
        new TemplateRecordDraft("@", RecordType.Txt, null, "v=spf1 mx ~all"),
        new TemplateRecordDraft("_dmarc", RecordType.Txt, null, "v=DMARC1; p=none"),
    ];

    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The clock the creation instant is taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seeder.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="clock">The clock.</param>
    public ZoneTemplateSeeder(DnsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Writes the built-in templates that are missing and chooses the default if none is chosen.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        await AddIfMissingAsync(WebTemplateId, WebName, "A site: the domain and www point at this server.", WebRecords, now, cancellationToken);
        await AddIfMissingAsync(
            WebAndMailTemplateId,
            WebAndMailName,
            "A site and its mail: the web records plus an MX, the mail host, SPF and a monitoring-only DMARC.",
            WebAndMailRecords,
            now,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var settings = await _dbContext.DnsSettings
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        if (settings?.DefaultTemplateId is not null)
        {
            return;
        }

        var webExists = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .AnyAsync(row => row.Id == WebTemplateId, cancellationToken);
        if (!webExists)
        {
            return;
        }

        if (settings is null)
        {
            settings = new DnsSettings(now);
            _dbContext.DnsSettings.Add(settings);
        }

        settings.ChooseDefaultTemplate(WebTemplateId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds one built-in template unless its row, or a template of that name, exists.</summary>
    /// <param name="id">The template's fixed identity.</param>
    /// <param name="name">The template's name.</param>
    /// <param name="description">What the template is for.</param>
    /// <param name="records">The template's records.</param>
    /// <param name="now">The creation instant.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    private async Task AddIfMissingAsync(
        Guid id,
        string name,
        string description,
        IReadOnlyList<TemplateRecordDraft> records,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var key = TemplateNamePolicy.Key(name);
        var present = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .AnyAsync(row => row.Id == id || row.NameKey == key, cancellationToken);
        if (present)
        {
            return;
        }

        var template = new ZoneTemplate(id, name, description, isBuiltIn: true, now);
        template.Replace(name, description, records, now);
        _dbContext.ZoneTemplates.Add(template);
    }
}
