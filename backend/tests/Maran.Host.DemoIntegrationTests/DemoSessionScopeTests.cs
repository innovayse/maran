using System.Net;
using Maran.Host.DemoIntegrationTests.Fixtures;

namespace Maran.Host.DemoIntegrationTests;

/// <summary>
/// One visitor signing out signs out only their own session, never another visitor's on the shared login.
/// </summary>
[Collection(DemoSharedDatabase.Name)]
public sealed class DemoSessionScopeTests
{
    /// <summary>The shared server.</summary>
    private readonly DemoPostgres _postgres;

    /// <summary>Creates the class over the shared server.</summary>
    /// <param name="postgres">The shared server.</param>
    public DemoSessionScopeTests(DemoPostgres postgres)
    {
        _postgres = postgres;
    }

    /// <summary>Visitor A signs out; visitor B's session still refreshes, and A's no longer does.</summary>
    [Fact]
    public async Task Logging_out_revokes_exactly_one_session()
    {
        await using var demo = await SeededDemo.StartAsync(_postgres);
        var (visitorA, refreshCookieA) = await demo.SignInKeepingRefreshCookieAsync("198.51.100.1");
        using var signedInA = visitorA;
        var (visitorB, refreshCookieB) = await demo.SignInKeepingRefreshCookieAsync("198.51.100.2");
        using var signedInB = visitorB;

        // The refresh cookie is Secure and the test client talks plain http, so its cookie jar never sends
        // it back: each cookie is presented by hand, as a browser on https would present it.
        var logout = await SendWithCookieAsync(visitorA, "/api/v1/auth/logout", refreshCookieA);
        var refreshA = await SendWithCookieAsync(visitorA, "/api/v1/auth/refresh", refreshCookieA);
        var refreshB = await SendWithCookieAsync(visitorB, "/api/v1/auth/refresh", refreshCookieB);

        Assert.True(logout.IsSuccessStatusCode, $"Logout answered {(int)logout.StatusCode}.");
        Assert.Equal(HttpStatusCode.Unauthorized, refreshA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshB.StatusCode);
    }

    /// <summary>Posts to <paramref name="route"/> presenting <paramref name="cookie"/> by hand.</summary>
    /// <param name="client">The visitor's client.</param>
    /// <param name="route">The route to post to.</param>
    /// <param name="cookie">The cookie, as <c>name=value</c>.</param>
    /// <returns>The answer.</returns>
    private static async Task<HttpResponseMessage> SendWithCookieAsync(HttpClient client, string route, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }
}
