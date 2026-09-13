using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Jobs;

/// <summary>
/// What the unattended renewal pass leaves in the panel's task journal. Nobody is watching a
/// renewal while it runs, so the tasks it leaves are how an operator opening the panel the next
/// morning sees which domains renewed and which quietly did not.
/// </summary>
public sealed class CertificateRenewalTaskRecordingTests
{
    /// <summary>A renewal pass leaves one task per certificate it attempted.</summary>
    [Fact]
    public async Task A_renewal_pass_leaves_one_task_per_certificate_it_attempted()
    {
        // Two due and one not: the pass must record the work it actually did, so the certificate it
        // correctly left alone must leave no task either.
        using var fixture = new SslHandlerFixture(["one.example.com", "two.example.com", "later.example.com"]);
        await SeedAsync(fixture, "one.example.com", 5);
        await SeedAsync(fixture, "two.example.com", 10);
        await SeedAsync(fixture, "later.example.com", 60);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(2, fixture.Tasks.Tasks.Count);
        Assert.All(fixture.Tasks.Tasks, task =>
        {
            Assert.Equal(TaskKinds.CertificateRenewal, task.Kind);
            Assert.True(task.Completed);
        });

        // No request behind an unattended pass, so no correlation id may be invented for one.
        Assert.All(fixture.Tasks.Tasks, task =>
        {
            Assert.Null(task.CorrelationId);
        });
    }

    /// <summary>A renewal the authority refused leaves a task carrying the code the row records.</summary>
    [Fact]
    public async Task A_renewal_the_authority_refused_leaves_a_task_carrying_the_code_the_row_records()
    {
        using var fixture = new SslHandlerFixture(
            ["soon.example.com"], acmeFailure: Error.Of("AcmeAuthorityUnreachable", ErrorType.Unavailable));
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var task = Assert.Single(fixture.Tasks.Tasks);
        Assert.Equal("soon.example.com", task.Subject);
        Assert.Equal("AcmeAuthorityUnreachable", task.FailureCode);
        Assert.False(task.Completed);
    }

    /// <summary>A renewal that threw leaves a closed task rather than one running for ever.</summary>
    /// <remarks>
    /// The reason the task is opened in the frame that owns every ending rather than one level down.
    /// A throw is caught so that one unreachable domain does not end the pass for every certificate
    /// behind it — and a task opened below that catch would be left Running for ever, showing an
    /// operator a renewal in flight that stopped hours ago.
    /// </remarks>
    [Fact]
    public async Task A_renewal_that_threw_leaves_a_closed_task_rather_than_one_running_for_ever()
    {
        using var fixture = new SslHandlerFixture(["boom.example.com", "fine.example.com"]);
        await SeedAsync(fixture, "boom.example.com", 5);
        await SeedAsync(fixture, "fine.example.com", 6);
        var job = HandlerWithAcme(fixture, new ThrowingAcmeClient("boom.example.com", fixture.Acme.Material));

        await job.HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var thrown = fixture.Tasks.Tasks.Single(task =>
        {
            return string.Equals(task.Subject, "boom.example.com", StringComparison.Ordinal);
        });

        Assert.Equal("AcmeAuthorityUnreachable", thrown.FailureCode);
        Assert.False(thrown.Completed);

        // And the pass carried on: the certificate behind it in the queue got its own finished task.
        var survivor = fixture.Tasks.Tasks.Single(task =>
        {
            return string.Equals(task.Subject, "fine.example.com", StringComparison.Ordinal);
        });

        Assert.True(survivor.Completed);
    }

    /// <summary>Seeds one certificate for a fixture's site.</summary>
    /// <param name="fixture">The fixture to seed.</param>
    /// <param name="domain">The domain, which must be one the fixture owns.</param>
    /// <param name="daysToExpiry">How far in the future the certificate expires.</param>
    /// <returns>Resolves once the row is stored.</returns>
    private static async Task SeedAsync(SslHandlerFixture fixture, string domain, int daysToExpiry)
    {
        fixture.DbContext.Certificates.Add(SslTestContext.Certificate(
            fixture.AccountId,
            domain,
            SslHandlerFixture.Now.AddDays(daysToExpiry),
            CertificateSource.Acme,
            fixture.SiteFor(domain).Id));
        await fixture.DbContext.SaveChangesAsync();
    }

    /// <summary>Builds the job over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The job under test.</returns>
    private static CertificateRenewalHandler HandlerFor(SslHandlerFixture fixture)
    {
        return HandlerWithAcme(fixture, fixture.Acme);
    }

    /// <summary>Builds the job over a fixture but with a different acme client.</summary>
    /// <param name="fixture">The fixture supplying everything else.</param>
    /// <param name="acme">The certificate authority double to use.</param>
    /// <returns>The job under test.</returns>
    private static CertificateRenewalHandler HandlerWithAcme(SslHandlerFixture fixture, IAcmeClient acme)
    {
        return new CertificateRenewalHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            acme,
            fixture.Installer,
            fixture.AgentSites,
            fixture.Journal,
            fixture.Clock,
            NullLogger<CertificateRenewalHandler>.Instance,
            fixture.Tasks);
    }
}
