using System.Net;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Services;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>What the receiving panel sends to another panel and how it reads the answer (issue #127).</summary>
public sealed class MigrationPullClientTests
{
    /// <summary>A grant-shaped token for the tests.</summary>
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>A handler that answers every request with one response and keeps the request it saw.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        /// <summary>The status to answer.</summary>
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        /// <summary>The request that arrived.</summary>
        public HttpRequestMessage? Seen { get; private set; }

        /// <summary>Whether the handler should fail as an unreachable host does.</summary>
        public bool Unreachable { get; init; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;

            if (Unreachable)
            {
                throw new HttpRequestException("connection refused");
            }

            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    /// <summary>A factory that hands out a client over the scripted handler.</summary>
    private sealed class FixedFactory : IHttpClientFactory
    {
        /// <summary>The handler every client sends through.</summary>
        private readonly HttpMessageHandler _handler;

        /// <summary>Creates the factory.</summary>
        /// <param name="handler">The handler every client sends through.</param>
        public FixedFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        /// <inheritdoc />
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    /// <summary>Opens the head through a client over <paramref name="handler"/>.</summary>
    /// <param name="handler">The scripted handler.</param>
    /// <param name="origin">The origin to pull from.</param>
    /// <returns>The client's answer.</returns>
    private static Task<Result<Stream>> OpenAsync(ScriptedHandler handler, string origin = "https://old.example.com")
    {
        var client = new MigrationPullClient(new FixedFactory(handler), NullLogger<MigrationPullClient>.Instance);

        return client.OpenAsync(origin, PullPart.Head, new SensitiveString(Token), CancellationToken.None);
    }

    /// <summary>The accepting control: a 200 opens the body, the grant is only in the Authorization header, and the URL carries nothing of it.</summary>
    [Fact]
    public async Task The_grant_travels_only_in_the_authorization_header()
    {
        var handler = new ScriptedHandler();

        var opened = await OpenAsync(handler);

        Assert.True(opened.IsSuccess);
        await using var body = opened.Value!;
        Assert.Equal("Bearer " + Token, handler.Seen!.Headers.Authorization!.ToString());
        Assert.Equal("https://old.example.com/api/v1/migration-pull/head", handler.Seen.RequestUri!.ToString());
        Assert.DoesNotContain(Token, handler.Seen.RequestUri.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The package part asks the package endpoint.</summary>
    [Fact]
    public async Task The_package_part_asks_the_package_endpoint()
    {
        var handler = new ScriptedHandler();
        var client = new MigrationPullClient(new FixedFactory(handler), NullLogger<MigrationPullClient>.Instance);

        var opened = await client.OpenAsync("https://old.example.com:8443", PullPart.Package, new SensitiveString(Token), CancellationToken.None);

        Assert.True(opened.IsSuccess);
        await using var body = opened.Value!;
        Assert.Equal("https://old.example.com:8443/api/v1/migration-pull/package", handler.Seen!.RequestUri!.ToString());
    }

    /// <summary>A grant is never sent over plain http or to an origin carrying a path or user information: nothing is sent at all.</summary>
    /// <param name="origin">The origin.</param>
    [Theory]
    [InlineData("http://old.example.com")]
    [InlineData("https://user@old.example.com")]
    [InlineData("https://old.example.com/path")]
    [InlineData("ftp://old.example.com")]
    public async Task An_origin_that_is_not_a_bare_https_origin_is_refused_without_a_request(string origin)
    {
        var handler = new ScriptedHandler();

        var opened = await OpenAsync(handler, origin);

        Assert.False(opened.IsSuccess);
        Assert.Equal("MigrationPullAddressRefused", opened.Error!.Code);
        Assert.Null(handler.Seen);
    }

    /// <summary>A 401 or 404 is the grant being refused; any other status, a redirect included, and an unreachable host are the unreachable failure.</summary>
    [Fact]
    public async Task Statuses_and_failures_are_told_apart_by_what_the_administrator_can_do()
    {
        Assert.Equal("MigrationPullRefused", (await OpenAsync(new ScriptedHandler { Status = HttpStatusCode.NotFound })).Error!.Code);
        Assert.Equal("MigrationPullRefused", (await OpenAsync(new ScriptedHandler { Status = HttpStatusCode.Unauthorized })).Error!.Code);
        Assert.Equal("MigrationPullUnreachable", (await OpenAsync(new ScriptedHandler { Status = HttpStatusCode.Found })).Error!.Code);
        Assert.Equal("MigrationPullUnreachable", (await OpenAsync(new ScriptedHandler { Status = HttpStatusCode.InternalServerError })).Error!.Code);
        Assert.Equal("MigrationPullUnreachable", (await OpenAsync(new ScriptedHandler { Unreachable = true })).Error!.Code);
    }
}
