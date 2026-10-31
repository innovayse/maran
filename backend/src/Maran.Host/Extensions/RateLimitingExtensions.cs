using System.Globalization;
using System.Runtime.CompilerServices;
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

    /// <summary>The media type of the panel's problem answers (RFC 7807), the rejection included.</summary>
    private const string ProblemContentType = "application/problem+json";

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
            InvitationRateLimitPolicy.Configure(options, limits);
            BackupRestoreRateLimitPolicy.Configure(options, limits);

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

                // The media type is passed to the writer rather than set on the response first:
                // WriteAsJsonAsync without a contentType overwrites it with application/json, which
                // is how this answer once carried the problem fields under the wrong media type
                // (issue #260).
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        status = StatusCodes.Status429TooManyRequests,
                        title = ResolveRateLimitedText(context.HttpContext),
                        code = RateLimitedCode,
                    },
                    options: null,
                    contentType: ProblemContentType,
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Adds ASP.NET Core's rate-limiting middleware so that, once the host has stopped, the limiter
    /// it creates no longer holds the host (issue #124).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this works around is the framework's.</b> The middleware builds a partitioned
    /// limiter when the pipeline is built, and that limiter runs a 100 ms replenishment timer until it
    /// is disposed. The middleware is not disposable and nothing disposes the limiter, so the timer —
    /// rooted by the process-wide timer queue — outlives the host, and the limiter's partition
    /// function holds the middleware, whose <c>next</c> is the rest of the pipeline and through it the
    /// whole container. (A second path, through the execution context the timer captured, is cut for
    /// every startup timer at once in <c>Program.Main</c>.)
    /// </para>
    /// <para>
    /// <b>What that cost, measured.</b> A panel builds its pipeline once, so in production this is one
    /// timer for the life of the process. A test process builds one host per test, and every host it
    /// had disposed stayed reachable: a heap snapshot mid-run held one live <c>Host</c>,
    /// <c>TestServer</c> and <c>WolverineRuntime</c> per finished test (61 after about a hundred tests),
    /// <c>gcroot</c> led each through this limiter, and with this one middleware removed the same
    /// snapshot held 4. The integration suite grew past a 6 GB cap on a workstation and took GitHub's
    /// 16 GB runners down with it.
    /// </para>
    /// <para>
    /// <b>How the path is cut.</b> The middleware is built in a branch whose terminal step reaches the
    /// rest of the pipeline through a holder that is emptied when the application stops, so after
    /// shutdown the limiter keeps the middleware and nothing behind it. While the host runs, a request
    /// passes through exactly as it did through <c>UseRateLimiter()</c> in this position: after routing
    /// and authentication, so endpoint policies and the caller's identity are both visible to it. A
    /// request still in flight after the application has stopped gets an empty response instead of
    /// running the rest of a pipeline that is being torn down.
    /// </para>
    /// </remarks>
    /// <param name="app">The application pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UsePanelRateLimiting(this IApplicationBuilder app)
    {
        var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();

        return app.Use(next =>
        {
            var rest = new StrongBox<RequestDelegate?>(next);
            lifetime.ApplicationStopped.Register(() => { rest.Value = null; });

            var branch = app.New();
            branch.UseRateLimiter();
            branch.Run(context =>
            {
                return rest.Value is { } downstream ? downstream(context) : Task.CompletedTask;
            });

            return branch.Build();
        });
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
