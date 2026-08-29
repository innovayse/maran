using System.Globalization;
using Maran.Host.Middleware;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Tests.Middleware;

/// <summary>Behavioral contract of <see cref="Host.Middleware.RequestLocalizationMiddleware"/>.</summary>
public sealed class RequestLocalizationMiddlewareTests
{
    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("hy", "hy")]
    [InlineData("en", "en")]
    public async Task Supported_accept_language_selects_that_culture(string header, string expectedCulture)
    {
        var observedCulture = await InvokeAndCaptureCultureAsync(header);

        Assert.Equal(expectedCulture, observedCulture.Name);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task Unsupported_or_absent_accept_language_falls_back_to_english(string header)
    {
        var observedCulture = await InvokeAndCaptureCultureAsync(header);

        Assert.Equal("en", observedCulture.Name);
    }

    [Fact]
    public async Task Quality_ordered_header_prefers_the_highest_ranked_supported_culture()
    {
        var observedCulture = await InvokeAndCaptureCultureAsync("fr;q=0.9, hy;q=0.5, ru;q=0.8");

        Assert.Equal("ru", observedCulture.Name);
    }

    /// <summary>Runs the middleware with the given <c>Accept-Language</c> header and returns the ambient culture seen downstream.</summary>
    /// <param name="acceptLanguageHeader">Raw header value; not added at all when empty.</param>
    private static async Task<CultureInfo> InvokeAndCaptureCultureAsync(string acceptLanguageHeader)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(acceptLanguageHeader))
        {
            context.Request.Headers.AcceptLanguage = acceptLanguageHeader;
        }

        CultureInfo? observed = null;
        var middleware = new RequestLocalizationMiddleware(_ =>
        {
            observed = CultureInfo.CurrentCulture;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        return observed!;
    }
}
