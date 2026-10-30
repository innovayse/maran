using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Queries.ListCertificates;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.Queries.ListCertificates;

/// <summary>
/// The list query, driven against an InMemory database holding two customers' rows and a fixed clock.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; <c>CertificateListEndpointTests</c> covers it against a real server.
/// </remarks>
public sealed class ListCertificatesQueryHandlerTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A customer sees only their own certificates soonest expiry first.</summary>
    [Fact]
    public async Task A_customer_sees_only_their_own_certificates_soonest_expiry_first()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = SslTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Certificates.Add(SslTestContext.Certificate(mine, "late.example.com", Now.AddDays(80)));
            seeding.Certificates.Add(SslTestContext.Certificate(mine, "soon.example.com", Now.AddDays(10)));
            seeding.Certificates.Add(SslTestContext.Certificate(Guid.NewGuid(), "theirs.example.com", Now));
            await seeding.SaveChangesAsync();
        }

        await using var reading = SslTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await HandleAsync(reading, new ListCertificatesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(["soon.example.com", "late.example.com"], Domains(result.Value.Items));
        Assert.Equal(2, result.Value.Total);
    }

    /// <summary>A domain sort orders the page alphabetically, and its descending form reverses it.</summary>
    [Fact]
    public async Task A_domain_sort_orders_the_page_alphabetically_and_its_descending_form_reverses_it()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        var owner = Guid.NewGuid();
        dbContext.Certificates.AddRange(
            SslTestContext.Certificate(owner, "b.example.com", Now.AddDays(5)),
            SslTestContext.Certificate(owner, "c.example.com", Now.AddDays(1)),
            SslTestContext.Certificate(owner, "a.example.com", Now.AddDays(9)));
        await dbContext.SaveChangesAsync();

        var ascending = await HandleAsync(dbContext, new ListCertificatesQuery(Sort: SortRequest.Parse("domain")));
        var descending = await HandleAsync(dbContext, new ListCertificatesQuery(Sort: SortRequest.Parse("-domain")));

        Assert.Equal(["a.example.com", "b.example.com", "c.example.com"], Domains(ascending.Value.Items));
        Assert.Equal(["c.example.com", "b.example.com", "a.example.com"], Domains(descending.Value.Items));
    }

    /// <summary>A page holds only its slice, and carries the total across every page.</summary>
    [Fact]
    public async Task A_page_holds_only_its_slice_and_carries_the_total_across_every_page()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        var owner = Guid.NewGuid();
        for (var day = 1; day <= 12; day++)
        {
            dbContext.Certificates.Add(SslTestContext.Certificate(owner, $"d{day:00}.example.com", Now.AddDays(day)));
        }

        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListCertificatesQuery(Page: 2, PageSize: 10));

        Assert.Equal(["d11.example.com", "d12.example.com"], Domains(result.Value.Items));
        Assert.Equal(12, result.Value.Total);
    }

    /// <summary>The site filter answers only that site's certificates.</summary>
    [Fact]
    public async Task The_site_filter_answers_only_that_sites_certificates()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        var owner = Guid.NewGuid();
        var site = Guid.NewGuid();
        dbContext.Certificates.AddRange(
            SslTestContext.Certificate(owner, "shop.example.com", Now.AddDays(40), siteId: site),
            SslTestContext.Certificate(owner, "blog.example.com", Now.AddDays(40)));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListCertificatesQuery(SiteId: site));

        Assert.Equal(["shop.example.com"], Domains(result.Value.Items));
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>
    /// Each status filter answers exactly its own certificates, and the three together cover every row
    /// once: the states partition the list.
    /// </summary>
    [Fact]
    public async Task Each_status_filter_answers_exactly_its_own_certificates_and_the_three_partition_the_list()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        var owner = Guid.NewGuid();
        var failing = SslTestContext.Certificate(owner, "failing.example.com", Now.AddDays(5));
        failing.RenewalFailed("AcmeValidationFailed", Now.AddDays(-1));
        var expiredAndFailing = SslTestContext.Certificate(owner, "lapsed.example.com", Now.AddDays(-2));
        expiredAndFailing.RenewalFailed("AcmeValidationFailed", Now.AddDays(-3));
        dbContext.Certificates.AddRange(
            SslTestContext.Certificate(owner, "valid.example.com", Now.AddDays(60)),
            failing,
            expiredAndFailing,
            // Expiring at this very instant is expired: NotAfter is the last instant it is valid.
            SslTestContext.Certificate(owner, "now.example.com", Now));
        await dbContext.SaveChangesAsync();

        var valid = await HandleAsync(dbContext, new ListCertificatesQuery(Status: CertificateStatus.Valid));
        var renewalFailing = await HandleAsync(dbContext, new ListCertificatesQuery(Status: CertificateStatus.RenewalFailing));
        var expired = await HandleAsync(dbContext, new ListCertificatesQuery(Status: CertificateStatus.Expired));

        Assert.Equal(["valid.example.com"], Domains(valid.Value.Items));
        Assert.Equal(["failing.example.com"], Domains(renewalFailing.Value.Items));
        Assert.Equal(["lapsed.example.com", "now.example.com"], Domains(expired.Value.Items));
        Assert.Equal(4, valid.Value.Total + renewalFailing.Value.Total + expired.Value.Total);
    }

    /// <summary>
    /// The expiring filter answers what has not expired yet and expires inside the renewal window,
    /// its far edge included, and nothing already expired.
    /// </summary>
    [Fact]
    public async Task The_expiring_filter_answers_what_expires_inside_the_renewal_window_and_nothing_already_expired()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        var owner = Guid.NewGuid();
        dbContext.Certificates.AddRange(
            SslTestContext.Certificate(owner, "edge.example.com", Now.AddDays(30)),
            SslTestContext.Certificate(owner, "soon.example.com", Now.AddDays(3)),
            SslTestContext.Certificate(owner, "beyond.example.com", Now.AddDays(30).AddSeconds(1)),
            SslTestContext.Certificate(owner, "lapsed.example.com", Now.AddDays(-1)));
        await dbContext.SaveChangesAsync();

        var result = await HandleAsync(dbContext, new ListCertificatesQuery(Expiring: true));

        Assert.Equal(["soon.example.com", "edge.example.com"], Domains(result.Value.Items));
    }

    /// <summary>
    /// The status enum has exactly the three members the handler filters by: a member added without a
    /// filter arm would fall through to "every status" and fail here instead.
    /// </summary>
    [Fact]
    public void Every_status_member_has_a_filter()
    {
        Assert.Equal(
            [CertificateStatus.Valid, CertificateStatus.RenewalFailing, CertificateStatus.Expired],
            Enum.GetValues<CertificateStatus>());
    }

    /// <summary>Runs the handler at the fixed instant.</summary>
    /// <param name="dbContext">The context to read.</param>
    /// <param name="query">The list request.</param>
    /// <returns>The handler's answer.</returns>
    private static Task<Result<PagedResult<CertificateDto>>> HandleAsync(SslDbContext dbContext, ListCertificatesQuery query)
    {
        return new ListCertificatesQueryHandler(dbContext, new FakeClock(Now)).HandleAsync(query, CancellationToken.None);
    }

    /// <summary>The domains of a page, in order.</summary>
    /// <param name="items">The page's rows.</param>
    /// <returns>Each row's domain.</returns>
    private static IEnumerable<string> Domains(IEnumerable<CertificateDto> items)
    {
        return items.Select(certificate =>
        {
            return certificate.Domain;
        });
    }
}
