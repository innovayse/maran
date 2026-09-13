using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.Commands.IssueCertificate;

/// <summary>
/// What issuing a certificate leaves in the panel's task journal. An order is the panel's slowest
/// foreground operation — an authority to reach, a challenge to serve, a poll to wait out — so it is
/// one an operator watches rather than waits on, and its record has to agree with the answer the
/// caller got.
/// </summary>
public sealed class IssueCertificateTaskRecordingTests
{
    /// <summary>The domain every test in this class issues for.</summary>
    private const string Domain = "example.com";

    /// <summary>A completed issuance leaves exactly one task naming the domain and closed as finished.</summary>
    [Fact]
    public async Task A_completed_issuance_leaves_exactly_one_task_naming_the_domain_and_closed_as_finished()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(fixture.Tasks.Tasks);
        Assert.Equal(TaskKinds.CertificateIssue, task.Kind);
        Assert.Equal(Domain, task.Subject);
        Assert.Equal(SslHandlerFixture.Correlation, task.CorrelationId);
        Assert.True(task.Completed);
        Assert.Null(task.FailureCode);
    }

    /// <summary>An issuance the authority refused leaves one task carrying the same error the response did.</summary>
    /// <remarks>
    /// The agreement is the whole point: a task closed under a different code — or left open — makes
    /// the pane and the response two accounts of one event, with no way to tell which is true.
    /// </remarks>
    [Fact]
    public async Task An_issuance_the_authority_refused_leaves_one_task_carrying_the_same_error_the_response_did()
    {
        using var fixture = new SslHandlerFixture([Domain], acmeFailure: Error.Of("AcmeAuthorityUnreachable", ErrorType.Unavailable));

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        var task = Assert.Single(fixture.Tasks.Tasks);
        Assert.Equal("AcmeAuthorityUnreachable", result.Error!.Code);
        Assert.Equal(result.Error!.Code, task.FailureCode);
        Assert.False(task.Completed);
    }

    /// <summary>An issuance for a domain the caller does not own leaves one task closed as not found.</summary>
    /// <remarks>
    /// The task exists here, unlike a deletion of an unknown account, because the domain the caller
    /// named IS the subject either way — an operator reading "example.com, not found" learns
    /// something, where "the account 3f2a…, not found" tells them nothing they can act on.
    /// </remarks>
    [Fact]
    public async Task An_issuance_for_a_domain_the_caller_does_not_own_leaves_one_task_closed_as_not_found()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(
            Command("somebody-elses.example"), CancellationToken.None);

        var task = Assert.Single(fixture.Tasks.Tasks);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Equal(result.Error!.Code, task.FailureCode);
        Assert.Equal("somebody-elses.example", task.Subject);
    }

    /// <summary>An issuance reports its stages in order and never goes backwards.</summary>
    [Fact]
    public async Task An_issuance_reports_its_stages_in_order_and_never_goes_backwards()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        var task = Assert.Single(fixture.Tasks.Tasks);
        var percentages = task.Reports.Select(report =>
        {
            return report.Percent;
        }).ToList();

        Assert.NotEmpty(percentages);
        Assert.Equal(percentages.Order(), percentages);
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="domain">The domain to issue for.</param>
    /// <returns>The command.</returns>
    private static IssueCertificateCommand Command(string domain = Domain)
    {
        return new IssueCertificateCommand(domain, "203.0.113.7", "tests");
    }

    /// <summary>Builds the handler over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The handler under test.</returns>
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
}
