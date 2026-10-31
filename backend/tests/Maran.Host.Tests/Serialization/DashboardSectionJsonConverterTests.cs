using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Host.Dashboard;

namespace Maran.Host.Tests.Serialization;

/// <summary>
/// Covers the wire form of a dashboard section: <c>{"state":"loaded","data":…}</c> or
/// <c>{"state":"unavailable"}</c>, under the same options the panel's HTTP surface uses.
/// </summary>
public sealed class DashboardSectionJsonConverterTests
{
    /// <summary>The panel's own web options: camelCase members and camelCase enum names.</summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>A loaded section is written as its state and its data.</summary>
    [Fact]
    public void A_loaded_section_is_written_with_its_state_and_data()
    {
        var json = JsonSerializer.Serialize(DashboardSection.Loaded<int>(5), Options);

        Assert.Equal("""{"state":"loaded","data":5}""", json);
    }

    /// <summary>A loaded zero keeps its data member: zero is an answer.</summary>
    [Fact]
    public void A_loaded_zero_is_written_with_its_data()
    {
        var json = JsonSerializer.Serialize(DashboardSection.Loaded<int>(0), Options);

        Assert.Equal("""{"state":"loaded","data":0}""", json);
    }

    /// <summary>
    /// An unavailable section carries no data member at all — not a null, not a zero, not an empty
    /// list — so nothing on the wire can be read as an answer.
    /// </summary>
    [Fact]
    public void An_unavailable_section_is_written_with_no_data_member()
    {
        var json = JsonSerializer.Serialize(DashboardSection.Unavailable<IReadOnlyList<string>>(), Options);

        Assert.Equal("""{"state":"unavailable"}""", json);
    }

    /// <summary>A loaded listing writes its items through the same options.</summary>
    [Fact]
    public void A_loaded_listing_is_written_with_its_items()
    {
        IReadOnlyList<string> items = ["a", "b"];

        var json = JsonSerializer.Serialize(DashboardSection.Loaded<IReadOnlyList<string>>(items), Options);

        Assert.Equal("""{"state":"loaded","data":["a","b"]}""", json);
    }

    /// <summary>Both forms read back as the section they were written from.</summary>
    [Fact]
    public void Both_forms_round_trip()
    {
        var loaded = JsonSerializer.Deserialize<DashboardSection<int>>("""{"state":"loaded","data":4}""", Options)!;
        var unavailable = JsonSerializer.Deserialize<DashboardSection<int>>("""{"state":"unavailable"}""", Options)!;

        Assert.Equal(4, loaded.Match(data =>
        {
            return data;
        }, () =>
        {
            return -1;
        }));
        Assert.Equal(DashboardSectionState.Unavailable, unavailable.State);
    }

    /// <summary>
    /// A section whose data contradicts its state is refused rather than guessed at: loaded without
    /// data, or unavailable with data.
    /// </summary>
    /// <param name="json">The contradictory section.</param>
    [Theory]
    [InlineData("""{"state":"loaded"}""")]
    [InlineData("""{"state":"unavailable","data":0}""")]
    [InlineData("""{"data":3}""")]
    public void A_section_whose_data_contradicts_its_state_is_refused(string json)
    {
        Assert.Throws<JsonException>(() =>
        {
            return JsonSerializer.Deserialize<DashboardSection<int>>(json, Options);
        });
    }
}
