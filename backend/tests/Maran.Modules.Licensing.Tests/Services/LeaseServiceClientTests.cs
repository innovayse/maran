using System.Net;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LeaseServiceClient"/>, the only type of the module that reaches the network: what the request
/// carries (the licence id and nothing else), where it may go (an https origin from configuration), what it does
/// with every kind of answer, and that the body it reads is bounded. The handler is a scripted stand-in under the
/// real client; no socket is opened.
/// </summary>
public sealed class LeaseServiceClientTests
{
    /// <summary>A valid origin the tests send to.</summary>
    private const string Origin = "https://licence.example.test";

    /// <summary>Everything the handler saw of one request.</summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Uri">The address.</param>
    /// <param name="HeaderNames">The names of the request headers, content headers excluded.</param>
    /// <param name="ContentType">The content type of the body.</param>
    /// <param name="Body">The body text.</param>
    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        IReadOnlyList<string> HeaderNames,
        string? ContentType,
        string Body);

    /// <summary>A body that does not declare its length, as a chunked answer does.</summary>
    private sealed class UnknownLengthContent : HttpContent
    {
        /// <summary>The bytes the body holds.</summary>
        private readonly byte[] _bytes;

        /// <summary>Creates the body.</summary>
        /// <param name="bytes">The bytes it holds.</param>
        public UnknownLengthContent(byte[] bytes)
        {
            _bytes = bytes;
        }

        /// <inheritdoc />
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_bytes.AsMemory()).AsTask();
        }

        /// <inheritdoc />
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>The innermost handler: records the request and answers what the test scripts.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        /// <summary>Answers a request; replaced by each test.</summary>
        public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            _ => { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); };

        /// <summary>What it saw, in order.</summary>
        public List<CapturedRequest> Seen { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Seen.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Select(header => { return header.Key; }).Order(StringComparer.Ordinal).ToList(),
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return await Respond(cancellationToken);
        }
    }

    /// <summary>A factory that hands every name the same scripted handler and remembers the names asked for.</summary>
    private sealed class StubFactory : IHttpClientFactory
    {
        /// <summary>The handler every client sends through.</summary>
        private readonly ScriptedHandler _handler;

        /// <summary>Creates the factory.</summary>
        /// <param name="handler">The handler every client sends through.</param>
        public StubFactory(ScriptedHandler handler)
        {
            _handler = handler;
        }

        /// <summary>The names clients were created for.</summary>
        public List<string> Names { get; } = [];

        /// <inheritdoc />
        public HttpClient CreateClient(string name)
        {
            Names.Add(name);

            return new HttpClient(_handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }
    }

    /// <summary>Builds the client over a scripted handler.</summary>
    /// <param name="handler">The handler.</param>
    /// <param name="baseUrl">The configured origin.</param>
    /// <param name="factory">The factory, so a test can read the names asked for.</param>
    /// <returns>The client under test.</returns>
    private static LeaseServiceClient ClientOver(ScriptedHandler handler, string baseUrl, out StubFactory factory)
    {
        factory = new StubFactory(handler);

        return new LeaseServiceClient(
            factory,
            new Microsoft.Extensions.Options.OptionsWrapper<LeaseServiceOptions>(
                new LeaseServiceOptions { LeaseServiceBaseUrl = baseUrl }),
            NullLogger<LeaseServiceClient>.Instance);
    }

    /// <summary>Scripts a response with a body of the given bytes.</summary>
    /// <param name="status">The status.</param>
    /// <param name="content">The body.</param>
    /// <returns>The response.</returns>
    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, HttpContent? content = null)
    {
        return Task.FromResult(new HttpResponseMessage(status) { Content = content ?? new StringContent(string.Empty) });
    }

    /// <summary>The request is one POST to the lease path with the licence id as its whole body and no credential.</summary>
    [Fact]
    public async Task The_request_is_a_post_of_the_licence_id_and_carries_no_credential()
    {
        var handler = new ScriptedHandler();
        handler.Respond = _ => { return Respond(HttpStatusCode.OK, new StringContent("{}")); };
        var client = ClientOver(handler, Origin, out var factory);

        await client.RequestAsync("lic-001", CancellationToken.None);

        var seen = Assert.Single(handler.Seen);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal(new Uri("https://licence.example.test/api/v1/lease"), seen.Uri);
        Assert.Equal("{\"licence\":\"lic-001\"}", seen.Body);
        Assert.Equal("application/json", seen.ContentType);
        Assert.Equal(["Accept"], seen.HeaderNames);
        Assert.Equal([LeaseServiceOptions.HttpClientName], factory.Names);
    }

    /// <summary>A licence id with characters JSON must escape is sent as a string member, never spliced into the body.</summary>
    [Fact]
    public async Task A_licence_id_is_sent_as_an_escaped_string_member()
    {
        var handler = new ScriptedHandler();
        var client = ClientOver(handler, Origin, out _);

        await client.RequestAsync("a\"b\\c", CancellationToken.None);

        Assert.Equal("{\"licence\":\"a\\u0022b\\\\c\"}", Assert.Single(handler.Seen).Body);
    }

    /// <summary>A 200 answer arrives as received, with its body.</summary>
    [Fact]
    public async Task A_200_answer_is_received_with_its_body()
    {
        var handler = new ScriptedHandler();
        handler.Respond = _ => { return Respond(HttpStatusCode.OK, new StringContent("{\"payload\":{}}")); };
        var client = ClientOver(handler, Origin, out _);

        var answer = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.Received, answer.Kind);
        Assert.Equal("{\"payload\":{}}", answer.Body);
    }

    /// <summary>Statuses that say nothing about the licence are "unreachable"; the 200 beside them is the control.</summary>
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task A_timeout_a_rate_limit_or_a_server_failure_is_unreachable(int status)
    {
        var handler = new ScriptedHandler();
        handler.Respond = _ => { return Respond((HttpStatusCode)status); };
        var client = ClientOver(handler, Origin, out _);

        var answer = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.Unreachable, answer.Kind);
        Assert.Null(answer.Body);
    }

    /// <summary>Statuses that say the service will not issue a lease are "refused", a redirect included, and carry no body.</summary>
    [Theory]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Any_other_status_is_a_refusal_and_a_redirect_is_not_followed(int status)
    {
        var handler = new ScriptedHandler();
        handler.Respond = _ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"payload\":{}}") };
            response.Headers.Location = new Uri("https://elsewhere.example.test/api/v1/lease");

            return Task.FromResult(response);
        };
        var client = ClientOver(handler, Origin, out _);

        var answer = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.Refused, answer.Kind);
        Assert.Null(answer.Body);
        Assert.Single(handler.Seen);
    }

    /// <summary>A body of exactly the bound is read; one byte more, declared or not, is discarded.</summary>
    [Fact]
    public async Task A_body_over_the_bound_is_discarded_whether_or_not_it_declares_its_length()
    {
        var bound = LeaseStorageOptions.MaxLeaseFileBytes;
        var handler = new ScriptedHandler();
        var client = ClientOver(handler, Origin, out _);

        handler.Respond = _ => { return Respond(HttpStatusCode.OK, new ByteArrayContent(new byte[bound])); };
        var atBound = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.Received, atBound.Kind);
        Assert.Equal(bound, atBound.Body!.Length);

        handler.Respond = _ => { return Respond(HttpStatusCode.OK, new ByteArrayContent(new byte[bound + 1])); };
        var declared = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.TooLarge, declared.Kind);
        Assert.Null(declared.Body);

        handler.Respond = _ => { return Respond(HttpStatusCode.OK, new UnknownLengthContent(new byte[bound + 1])); };
        var undeclared = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.TooLarge, undeclared.Kind);

        handler.Respond = _ => { return Respond(HttpStatusCode.OK, new UnknownLengthContent(new byte[bound])); };
        var undeclaredAtBound = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.Received, undeclaredAtBound.Kind);
    }

    /// <summary>A connection failure, a request the pipeline ended and a broken body are all "unreachable".</summary>
    [Fact]
    public async Task A_failed_connection_a_timeout_and_a_broken_body_are_unreachable()
    {
        var handler = new ScriptedHandler();
        var client = ClientOver(handler, Origin, out _);

        foreach (Exception failure in new Exception[]
        {
            new HttpRequestException("connection refused"),
            new TaskCanceledException("deadline", new TimeoutException()),
            new IOException("reset"),
            new TimeoutException("pipeline deadline"),
        })
        {
            handler.Respond = _ => { throw failure; };

            var answer = await client.RequestAsync("lic-001", CancellationToken.None);

            Assert.Equal(LeaseServiceAnswerKind.Unreachable, answer.Kind);
        }
    }

    /// <summary>A failure that is not a network failure is a bug and is not swallowed.</summary>
    [Fact]
    public async Task An_unexpected_failure_is_not_swallowed()
    {
        var handler = new ScriptedHandler();
        handler.Respond = _ => { throw new InvalidOperationException("bug"); };
        var client = ClientOver(handler, Origin, out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => { return client.RequestAsync("lic-001", CancellationToken.None); });
    }

    /// <summary>When the caller cancels, the cancellation propagates instead of becoming an "unreachable" answer.</summary>
    [Fact]
    public async Task A_cancelled_request_is_cancelled_and_not_unreachable()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new ScriptedHandler();
        handler.Respond = async token =>
        {
            await cancel.CancelAsync();
            await Task.Delay(System.Threading.Timeout.Infinite, token);

            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var client = ClientOver(handler, Origin, out _);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => { return client.RequestAsync("lic-001", cancel.Token); });
    }

    /// <summary>An address that is not an https origin makes no request at all; the origin forms that are accepted are the control.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("licence.example.test")]
    [InlineData("http://licence.example.test")]
    [InlineData("https://user:secret@licence.example.test")]
    [InlineData("https://licence.example.test/some/path")]
    [InlineData("https://licence.example.test?x=1")]
    public async Task An_address_that_is_not_an_https_origin_sends_nothing(string baseUrl)
    {
        var handler = new ScriptedHandler();
        var client = ClientOver(handler, baseUrl, out var factory);

        var answer = await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.NotConfigured, answer.Kind);
        Assert.Empty(handler.Seen);
        Assert.Empty(factory.Names);
    }

    /// <summary>The accepted origin forms are used: a trailing slash is dropped and a port is kept.</summary>
    [Theory]
    [InlineData("https://licence.example.test", "https://licence.example.test/api/v1/lease")]
    [InlineData("https://licence.example.test/", "https://licence.example.test/api/v1/lease")]
    [InlineData("https://licence.example.test:8443", "https://licence.example.test:8443/api/v1/lease")]
    public async Task An_https_origin_is_where_the_request_goes(string baseUrl, string expected)
    {
        var handler = new ScriptedHandler();
        var client = ClientOver(handler, baseUrl, out _);

        await client.RequestAsync("lic-001", CancellationToken.None);

        Assert.Equal(new Uri(expected), Assert.Single(handler.Seen).Uri);
    }

    /// <summary>The documented default is the documented host.</summary>
    [Fact]
    public void The_default_address_is_the_documented_host()
    {
        Assert.Equal("https://api-marketplace-maran.innovayse.com", new LeaseServiceOptions().LeaseServiceBaseUrl);
    }

    /// <summary>The innermost handler follows no redirect and keeps no cookie.</summary>
    [Fact]
    public void The_handler_follows_no_redirect_and_keeps_no_cookie()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(LeaseServiceClient.CreateHandler());

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    /// <summary>The named client has no timeout of its own, so the pipeline's deadline is the only one.</summary>
    [Fact]
    public void The_named_client_leaves_the_deadline_to_the_pipeline()
    {
        using var http = new HttpClient();

        LeaseServiceClient.ConfigureClient(http);

        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, http.Timeout);
    }
}
