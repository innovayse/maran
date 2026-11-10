using Maran.Modules.Ssl.Queries.GetIssuanceOptions;

namespace Maran.Modules.Ssl.Tests.Queries.GetIssuanceOptions;

/// <summary>The options query holds the domain to the panel's host-name shape, and refuses nothing else.</summary>
public sealed class GetIssuanceOptionsQueryValidatorTests
{
    /// <summary>A well-formed host name passes.</summary>
    [Fact]
    public void A_well_formed_host_name_passes()
    {
        var result = new GetIssuanceOptionsQueryValidator().Validate(new GetIssuanceOptionsQuery("www.example.com"));

        Assert.True(result.IsValid);
    }

    /// <summary>An empty name, a malformed one and one with a trailing newline are refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a host")]
    [InlineData("example.com\n")]
    [InlineData("*.example.com")]
    public void A_name_that_is_not_a_host_name_is_refused(string domain)
    {
        var result = new GetIssuanceOptionsQueryValidator().Validate(new GetIssuanceOptionsQuery(domain));

        Assert.False(result.IsValid);
    }
}
