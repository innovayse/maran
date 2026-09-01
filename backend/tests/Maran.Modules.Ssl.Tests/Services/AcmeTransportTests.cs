using System.Net;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The retry policy for the two ACME requests that are safe to repeat, and the boundary of what it
/// will and will not repeat.
/// </summary>
/// <remarks>
/// This is the policy that used to live in the Host's HTTP pipeline, where it retried everything —
/// including signed POSTs, whose replay is guaranteed to be refused and whose replayed
/// <c>newOrder</c> can duplicate an order at the authority. Here it is a function of two arguments
/// with tests, rather than a predicate nobody could exercise.
/// </remarks>
public sealed class AcmeTransportTests
{
    /// <summary>A transport failure is retried once and then succeeds.</summary>
    [Fact]
    public async Task A_transport_failure_is_retried_once_and_then_succeeds()
    {
        var handler = new ScriptedHandler([null, HttpStatusCode.OK]);

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>A server error is retried once and then succeeds.</summary>
    [Fact]
    public async Task A_server_error_is_retried_once_and_then_succeeds()
    {
        var handler = new ScriptedHandler([HttpStatusCode.InternalServerError, HttpStatusCode.OK]);

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>A rate limit answer is returned rather than retried.</summary>
    [Fact]
    public async Task A_rate_limit_answer_is_returned_rather_than_retried()
    {
        // Asking again is the problem the authority is complaining about, and a retry spends more of
        // the budget that is already exhausted.
        var handler = new ScriptedHandler([(HttpStatusCode)429, HttpStatusCode.OK]);

        using var response = await SendAsync(handler);

        Assert.Equal((HttpStatusCode)429, response!.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>Any other client error is returned rather than retried.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Any_other_client_error_is_returned_rather_than_retried(HttpStatusCode status)
    {
        var handler = new ScriptedHandler([status, HttpStatusCode.OK]);

        using var response = await SendAsync(handler);

        Assert.Equal(status, response!.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>Two failures in a row give up rather than looping.</summary>
    [Fact]
    public async Task Two_failures_in_a_row_give_up_rather_than_looping()
    {
        var handler = new ScriptedHandler([null, null]);

        using var response = await SendAsync(handler);

        Assert.Null(response);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>Each attempt builds a fresh request because a message cannot be sent twice.</summary>
    [Fact]
    public async Task Each_attempt_builds_a_fresh_request_because_a_message_cannot_be_sent_twice()
    {
        var handler = new ScriptedHandler([HttpStatusCode.InternalServerError, HttpStatusCode.OK]);
        var built = 0;

        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await AcmeTransport.SendIdempotentAsync(
            http,
            "https://acme.test/directory",
            () =>
            {
                built++;
                return new HttpRequestMessage(HttpMethod.Get, new Uri("https://acme.test/directory"));
            },
            NullLogger<AcmeTransportTests>.Instance,
            CancellationToken.None);

        Assert.Equal(2, built);
    }

    /// <summary>Runs one idempotent send against a scripted handler.</summary>
    /// <param name="handler">The scripted handler to send through.</param>
    /// <returns>What the transport answered.</returns>
    private static async Task<HttpResponseMessage?> SendAsync(ScriptedHandler handler)
    {
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        return await AcmeTransport.SendIdempotentAsync(
            http,
            "https://acme.test/directory",
            () =>
            {
                return new HttpRequestMessage(HttpMethod.Get, new Uri("https://acme.test/directory"));
            },
            NullLogger<AcmeTransportTests>.Instance,
            CancellationToken.None);
    }
}
