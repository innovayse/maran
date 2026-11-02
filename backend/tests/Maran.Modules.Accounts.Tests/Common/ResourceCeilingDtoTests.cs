using System.Text.Json;
using Maran.Modules.Accounts.Common;

namespace Maran.Modules.Accounts.Tests.Common;

/// <summary>
/// Behavioural contract of the ceiling's wire shape: a figure travels only with a capped ceiling, and
/// the case is told apart by <c>kind</c> in both directions.
/// </summary>
public sealed class ResourceCeilingDtoTests
{
    /// <summary>The options the panel's API reads and writes with, for the parts that matter here.</summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>A capped ceiling is written with its kind and its figure.</summary>
    [Fact]
    public void A_capped_ceiling_is_written_with_its_kind_and_its_figure()
    {
        var json = JsonSerializer.Serialize<ResourceCeilingDto>(new LimitedResourceCeilingDto(512), Wire);

        Assert.Equal("""{"kind":"limited","maximum":512}""", json);
    }

    /// <summary>An uncapped ceiling is written with its kind and no figure at all.</summary>
    [Fact]
    public void An_uncapped_ceiling_is_written_with_its_kind_and_no_figure_at_all()
    {
        var json = JsonSerializer.Serialize<ResourceCeilingDto>(new UnlimitedResourceCeilingDto(), Wire);

        Assert.Equal("""{"kind":"unlimited"}""", json);
    }

    /// <summary>A capped ceiling is read back as the capped case with its figure.</summary>
    [Fact]
    public void A_capped_ceiling_is_read_back_as_the_capped_case_with_its_figure()
    {
        var read = JsonSerializer.Deserialize<ResourceCeilingDto>("""{"kind":"limited","maximum":64}""", Wire);

        Assert.Equal(new LimitedResourceCeilingDto(64), read);
    }

    /// <summary>An uncapped ceiling is read back as the uncapped case.</summary>
    [Fact]
    public void An_uncapped_ceiling_is_read_back_as_the_uncapped_case()
    {
        var read = JsonSerializer.Deserialize<ResourceCeilingDto>("""{"kind":"unlimited"}""", Wire);

        Assert.Equal(new UnlimitedResourceCeilingDto(), read);
    }

    /// <summary>A figure sent beside an uncapped kind is dropped, never read as a cap.</summary>
    [Fact]
    public void A_figure_sent_beside_an_uncapped_kind_is_dropped_never_read_as_a_cap()
    {
        var read = JsonSerializer.Deserialize<ResourceCeilingDto>("""{"kind":"unlimited","maximum":512}""", Wire);

        Assert.Equal(new UnlimitedResourceCeilingDto(), read);
    }

    /// <summary>A ceiling naming no kind is refused rather than read as one of the two.</summary>
    [Fact]
    public void A_ceiling_naming_no_kind_is_refused_rather_than_read_as_one_of_the_two()
    {
        // Which exception type the serializer raises is its own business; that it raises one, so the
        // request is answered as unreadable, is the contract.
        var refusal = Record.Exception(() =>
        {
            return JsonSerializer.Deserialize<ResourceCeilingDto>("""{"maximum":512}""", Wire);
        });

        Assert.NotNull(refusal);
    }

    /// <summary>A ceiling naming an unknown kind is refused.</summary>
    [Fact]
    public void A_ceiling_naming_an_unknown_kind_is_refused()
    {
        var refusal = Record.Exception(() =>
        {
            return JsonSerializer.Deserialize<ResourceCeilingDto>("""{"kind":"sometimes","maximum":1}""", Wire);
        });

        Assert.NotNull(refusal);
    }
}
