using System.Text.Json;
using Maran.Modules.Accounts.Common;

namespace Maran.Modules.Accounts.Tests.Common;

/// <summary>
/// Behavioural contract of the usage row's count on the wire: a number travels only with a count that
/// was taken, so "unavailable" can never be read as zero.
/// </summary>
public sealed class ResourceCountDtoTests
{
    /// <summary>The options the panel's API writes with, for the parts that matter here.</summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>A count that was taken is written with its state and its number, zero included.</summary>
    [Theory]
    [InlineData(0, """{"state":"counted","used":0}""")]
    [InlineData(7, """{"state":"counted","used":7}""")]
    public void A_count_that_was_taken_is_written_with_its_state_and_its_number(int used, string expected)
    {
        var json = JsonSerializer.Serialize<ResourceCountDto>(new CountedResourceCountDto(used), Wire);

        Assert.Equal(expected, json);
    }

    /// <summary>A count that could not be taken is written with its state and no number at all.</summary>
    [Fact]
    public void A_count_that_could_not_be_taken_is_written_with_its_state_and_no_number_at_all()
    {
        var json = JsonSerializer.Serialize<ResourceCountDto>(new UnavailableResourceCountDto(), Wire);

        Assert.Equal("""{"state":"unavailable"}""", json);
    }
}
