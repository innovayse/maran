using System.Globalization;
using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Host.RateLimiting;
using Microsoft.Extensions.Options;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the named rate-limiting policies. The panel sits on a public IP and is probed
/// constantly, so limiting is part of the product, not an optional hardening step
/// (rules/security.md).
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Machine-stable code of the rejection response, and the key of its
    /// <c>Resources/ErrorMessages*.resx</c> entry — one identifier, not a code plus a separate
    /// resource key that can drift apart (rules/csharp.md "That same string is the machine code").
    /// </summary>
    private const string RateLimitedCode = "HostRateLimited";

    /// <summary>Adds the login and API policies, plus the shared rejection response.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            var limits = services.BuildServiceProvider().GetRequiredService<IOptions<RateLimitOptions>>().Value;

            LoginRateLimitPolicy.Configure(options, limits);
            ApiRateLimitPolicy.Configure(options, limits);
            SiteLogStreamRateLimitPolicy.Configure(options, limits);
            PasswordResetRateLimitPolicy.Configure(options, limits);

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                // A rejected caller must learn when to come back; an empty 429 forces clients
                // (and honest users) to guess, and guessing means retry storms. Only some limiter
                // types publish RetryAfter lease metadata — a sliding window never does — so the
                // window length from configuration is used as the honest fallback.
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter)
                    ? (int)leaseRetryAfter.TotalSeconds
                    : FallbackRetryAfterSeconds(context.HttpContext, limits);

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        status = StatusCodes.Status429TooManyRequests,
                        title = ResolveRateLimitedText(context.HttpContext),
                        code = RateLimitedCode,
                    },
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Resolves the rejection message in the request's culture, the same way
    /// <c>ApiResultExtensions.ToProblemResult</c> does: the provider is looked up from the request
    /// services rather than assumed present, so a host built without the SharedKernel registration
    /// (an isolated test host) degrades to the machine code instead of throwing. The code is never
    /// a stack trace, a path or tool output, so it is safe to show (rules/security.md "Secrets").
    /// </summary>
    /// <param name="httpContext">The rejected request, whose services and culture are used.</param>
    /// <returns>The localized sentence, or <see cref="RateLimitedCode"/> when nothing resolves it.</returns>
    private static string ResolveRateLimitedText(HttpContext httpContext)
    {
        var errorTextProvider = httpContext.RequestServices.GetService<IErrorTextProvider>();
        return errorTextProvider?.Resolve(RateLimitedCode) ?? RateLimitedCode;
    }

    /// <summary>
    /// How long a rejected caller should wait when the limiter publishes no lease metadata. The
    /// configured window is the truthful answer: within it the caller's permits cannot recover.
    /// </summary>
    /// <param name="httpContext">The rejected request, used to tell the login path from the rest.</param>
    /// <param name="limits">Configured rate-limit windows.</param>
    /// <returns>Seconds to report in the <c>Retry-After</c> header.</returns>
    private static int FallbackRetryAfterSeconds(HttpContext httpContext, RateLimitOptions limits)
    {
        return httpContext.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase)
            ? limits.LoginLockoutSeconds
            : limits.ApiWindowSeconds;
    }
}
