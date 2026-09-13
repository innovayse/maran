using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Ssl.Tests.Commands.IssueCertificate;

/// <summary>The issuance handler, driven through its own entry point with the authority faked.</summary>
public sealed class IssueCertificateCommandHandlerTests
{
    /// <summary>The domain every test in this class issues for.</summary>
    private const string Domain = "example.com";

    /// <summary>Command.</summary>
    private static IssueCertificateCommand Command(string domain = Domain)
    {
        return new IssueCertificateCommand(domain, "203.0.113.7", "tests");
    }

    /// <summary>HandlerFor.</summary>
    private static IssueCertificateCommandHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new IssueCertificateCommandHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            fixture.Acme,
            fixture.Installer,
            fixture.Journal,
            fixture.Clock,
            fixture.Tasks,
            fixture.CorrelationIds);
    }

    /// <summary>Issuing stores the certificate with the expiry the agent read off the material.</summary>
    [Fact]
    public async Task Issuing_stores_the_certificate_with_the_expiry_the_agent_read_off_the_material()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CertificateSource.Acme, result.Value.Source);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), result.Value.NotAfter);
        Assert.Equal(SslHandlerFixture.Now, result.Value.IssuedAt);

        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Equal(Domain, stored.Domain);
        Assert.Equal(fixture.SiteFor(Domain).Id, stored.SiteId);
    }

    /// <summary>Issuing orders for the domain under the owning accounts system user.</summary>
    [Fact]
    public async Task Issuing_orders_for_the_domain_under_the_owning_accounts_system_user()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal(Domain, order.Domain);
        Assert.Equal(fixture.Username, order.AccountUsername);
    }

    /// <summary>Installing marks the site as carrying a certificate.</summary>
    [Fact]
    public async Task Installing_marks_the_site_as_carrying_a_certificate()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal([fixture.SiteFor(Domain).Id], fixture.Sites.Attached);
    }

    /// <summary>The vhost the agent is asked to write carries tls and the sites own facts.</summary>
    [Fact]
    public async Task The_vhost_the_agent_is_asked_to_write_carries_tls_and_the_sites_own_facts()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        var install = Assert.Single(fixture.Agent.Installs);
        Assert.True(install.Site.HasCertificate);
        Assert.Equal(["www." + Domain], install.Site.Aliases);
        Assert.Equal("8.3", install.Site.PhpVersion);
    }

    /// <summary>A domain the caller does not own is not found rather than forbidden.</summary>
    [Fact]
    public async Task A_domain_the_caller_does_not_own_is_not_found_rather_than_forbidden()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command("someone-else.example.com"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(fixture.Acme.Orders);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
    }

    /// <summary>A failed order leaves no row and installs nothing.</summary>
    [Fact]
    public async Task A_failed_order_leaves_no_row_and_installs_nothing()
    {
        using var fixture = new SslHandlerFixture([Domain], acmeFailure: Error.Of("AcmeValidationFailed", ErrorType.Failure));

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AcmeValidationFailed", result.Error!.Code);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
        Assert.Empty(fixture.Agent.Installs);
        Assert.Empty(fixture.Sites.Attached);
    }

    /// <summary>An agent that refuses the install leaves no row and no flag.</summary>
    [Fact]
    public async Task An_agent_that_refuses_the_install_leaves_no_row_and_no_flag()
    {
        using var fixture = new SslHandlerFixture([Domain], agentFailure: Error.Of("AgentValidationFailed", ErrorType.Validation));

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
        Assert.Empty(fixture.Sites.Attached);
    }

    /// <summary>A domain that already has a certificate is refused without spending an order.</summary>
    [Fact]
    public async Task A_domain_that_already_has_a_certificate_is_refused_without_spending_an_order()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.DbContext.Certificates.Add(
            SslTestContext.Certificate(fixture.AccountId, Domain, SslHandlerFixture.Now.AddDays(60)));
        await fixture.DbContext.SaveChangesAsync();

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("CertificateAlreadyExists", result.Error!.Code);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>A domain already certified under another account is still refused.</summary>
    [Fact]
    public async Task A_domain_already_certified_under_another_account_is_still_refused()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.DbContext.Certificates.Add(
            SslTestContext.Certificate(Guid.NewGuid(), Domain, SslHandlerFixture.Now.AddDays(60)));
        await fixture.DbContext.SaveChangesAsync();

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        // The tenant filter hides that row from this caller; the check must look past it or the
        // insert collides on the unique index as an unhandled exception.
        Assert.False(result.IsSuccess);
        Assert.Equal("CertificateAlreadyExists", result.Error!.Code);
    }

    /// <summary>An account the caller may not see refuses before any order is placed.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_refuses_before_any_order_is_placed()
    {
        using var fixture = new SslHandlerFixture([Domain], knowsAccount: false);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>Both a success and a refusal are journalled against the domain.</summary>
    [Fact]
    public async Task Both_a_success_and_a_refusal_are_journalled_against_the_domain()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);
        await HandlerFor(fixture).HandleAsync(Command("no-such.example.com"), CancellationToken.None);

        Assert.Equal(2, fixture.Audit.Entries.Count);
        Assert.True(fixture.Audit.Entries[0].Succeeded);
        Assert.Equal(Domain, fixture.Audit.Entries[0].Subject);
        Assert.False(fixture.Audit.Entries[1].Succeeded);
        Assert.Equal("no-such.example.com", fixture.Audit.Entries[1].Subject);
    }

    /// <summary>No journal entry ever carries the issued material.</summary>
    [Fact]
    public async Task No_journal_entry_ever_carries_the_issued_material()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.Acme.Material = new Maran.Modules.Ssl.Models.IssuedCertificate(
            "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
            "SUPER-SECRET-PRIVATE-KEY",
            SslHandlerFixture.Now.AddDays(90));

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        foreach (var entry in fixture.Audit.Entries)
        {
            Assert.DoesNotContain("SUPER-SECRET-PRIVATE-KEY", entry.Subject, StringComparison.Ordinal);
            Assert.DoesNotContain("SUPER-SECRET-PRIVATE-KEY", entry.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>A duplicate the database refuses is answered as a conflict rather than a server error.</summary>
    [Fact]
    public async Task A_duplicate_the_database_refuses_is_answered_as_a_conflict_rather_than_a_server_error()
    {
        // The check above the insert and the insert itself are not one step, so two simultaneous
        // requests for one domain can both pass the check. The unique index stops the second, and
        // this is where that arrives — as a typed 409, not an unhandled exception.
        using var fixture = new SslHandlerFixture([Domain], saveFailures: 1);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("CertificateAlreadyExists", result.Error!.Code);
    }

    /// <summary>A duplicate leaves the site flagged as carrying tls because it genuinely does.</summary>
    [Fact]
    public async Task A_duplicate_leaves_the_site_flagged_as_carrying_tls_because_it_genuinely_does()
    {
        // The loser of the race installed material for the same domain into the same slot the winner
        // did, so the flag is TRUE and the winner's row is the one renewal will use. Clearing the
        // flag here would drop a live site back to plain HTTP on its next unrelated edit.
        using var fixture = new SslHandlerFixture([Domain], saveFailures: 1);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal([fixture.SiteFor(Domain).Id], fixture.Sites.Attached);
        Assert.Single(fixture.Agent.Installs);
    }

    /// <summary>A duplicate is journalled as a refusal.</summary>
    [Fact]
    public async Task A_duplicate_is_journalled_as_a_refusal()
    {
        using var fixture = new SslHandlerFixture([Domain], saveFailures: 1);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(Domain, entry.Subject);
    }

    /// <summary>A database failure that is not a duplicate is not reported as an already issued certificate.</summary>
    [Fact]
    public async Task A_database_failure_that_is_not_a_duplicate_is_not_reported_as_an_already_issued_certificate()
    {
        // Only a UNIQUE VIOLATION has a winner whose row renewal will use. On any other failure —
        // here a serialization failure — the material is on disk, the site says it carries a
        // certificate, and no row exists at all, so renewal would never run and TLS would expire
        // silently in ~90 days. Swallowing it as CertificateAlreadyExists would also tell the
        // customer the very thing that stops them retrying, which is the one action that repairs it.
        using var fixture = new SslHandlerFixture(
            [Domain], saveFailures: 1, saveFailureSqlState: PostgresErrorCodes.SerializationFailure);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);
        });
    }
}
