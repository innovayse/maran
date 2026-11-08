using System.Globalization;
using System.Text.Json;
using Maran.Host.Extensions;
using Maran.Host.RateLimiting;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.RateLimiting;

/// <summary>
/// Behavioural contract of <see cref="RateLimitedProblemWriter"/>, the one writer behind the rate
/// limiter's rejection and the dashboard stream's process-wide limit.
/// </summary>
public sealed class RateLimitedProblemWriterTests
{
    /// <summary>The answer is a 429 problem document with the rate-limited code and a Retry-After header.</summary>
    [Fact]
    public async Task The_answer_is_a_429_problem_document_with_the_rate_limited_code_and_a_retry_after_header()
    {
        var context = CreateContext(withTextProvider: true);

        using var body = await WriteAsync(context, "en", retryAfterSeconds: 15);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("15", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("HostRateLimited", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>The sentence is written in the request culture.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task The_sentence_is_written_in_the_request_culture(string language)
    {
        using var body = await WriteAsync(CreateContext(withTextProvider: true), language, retryAfterSeconds: 1);

        var title = body.RootElement.GetProperty("title").GetString();

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.NotEqual(RateLimitedProblemWriter.Code, title);
    }

    /// <summary>A host with no error-text provider degrades to the machine code instead of throwing.</summary>
    [Fact]
    public async Task A_host_with_no_error_text_provider_degrades_to_the_machine_code()
    {
        using var body = await WriteAsync(CreateContext(withTextProvider: false), "en", retryAfterSeconds: 1);

        Assert.Equal("HostRateLimited", body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>Writes the answer in <paramref name="language"/> and parses the body.</summary>
    /// <param name="context">The refused request.</param>
    /// <param name="language">The request culture.</param>
    /// <param name="retryAfterSeconds">The wait to report.</param>
    private static async Task<JsonDocument> WriteAsync(DefaultHttpContext context, string language, int retryAfterSeconds)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(language);
            await RateLimitedProblemWriter.WriteAsync(context, retryAfterSeconds, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    /// <summary>Builds a request context, optionally resolving the Host's own error texts.</summary>
    /// <param name="withTextProvider">Whether the services carry the error-text provider.</param>
    private static DefaultHttpContext CreateContext(bool withTextProvider)
    {
        var services = new ServiceCollection();
        if (withTextProvider)
        {
            services
                .AddPanelLocalization()
                .AddSingleton<ILogger<ResxErrorTextProvider>>(NullLogger<ResxErrorTextProvider>.Instance)
                .AddSingleton<IErrorTextProvider, ResxErrorTextProvider>();
        }

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }
}
