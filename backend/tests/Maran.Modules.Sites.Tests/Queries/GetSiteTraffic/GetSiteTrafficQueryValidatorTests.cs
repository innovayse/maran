using Maran.Modules.Sites.Queries.GetSiteTraffic;

namespace Maran.Modules.Sites.Tests.Queries.GetSiteTraffic;

/// <summary>Behavioral contract of <see cref="GetSiteTrafficQueryValidator"/>.</summary>
public sealed class GetSiteTrafficQueryValidatorTests
{
    /// <summary>Each window the server lists, and no window at all, pass.</summary>
    /// <param name="days">The window, or null for the server's default.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public void Each_listed_window_and_no_window_at_all_pass(int? days)
    {
        var result = new GetSiteTrafficQueryValidator().Validate(new GetSiteTrafficQuery(days, Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    /// <summary>A window the server does not list is refused with the window code.</summary>
    /// <param name="days">The refused window.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(365)]
    [InlineData(-7)]
    public void A_window_the_server_does_not_list_is_refused_with_the_window_code(int days)
    {
        var result = new GetSiteTrafficQueryValidator().Validate(new GetSiteTrafficQuery(days, Guid.NewGuid()));

        Assert.Equal("SiteTrafficWindowInvalid", Assert.Single(result.Errors).ErrorMessage);
    }
}
