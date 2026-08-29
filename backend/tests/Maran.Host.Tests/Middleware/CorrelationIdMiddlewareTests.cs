using System.Net;
using Maran.SharedKernel.Constants;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.Tests.Middleware;

/// <summary>Behavioral contract of <see cref="Host.Middleware.CorrelationIdMiddleware"/>.</summary>
public sealed class CorrelationIdMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    public CorrelationIdMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(PanelTestSettings.EncryptionKeyPath, PanelTestSettings.EncryptionKey));
    }

    [Fact]
    public async Task Incoming_correlation_id_is_echoed_back_unchanged()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdKeys.HeaderName, "caller-supplied-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("caller-supplied-id", response.Headers.GetValues(CorrelationIdKeys.HeaderName).Single());
    }

    [Fact]
    public async Task Missing_correlation_id_is_minted_and_returned()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var minted = response.Headers.GetValues(CorrelationIdKeys.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(minted));
        Assert.True(Guid.TryParse(minted, out _));
    }

    [Fact]
    public async Task Every_response_carries_the_correlation_id_header()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        Assert.True(response.Headers.Contains(CorrelationIdKeys.HeaderName));
    }
}
