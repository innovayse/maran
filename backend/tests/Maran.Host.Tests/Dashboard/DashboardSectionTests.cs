using Maran.Host.Dashboard;

namespace Maran.Host.Tests.Dashboard;

/// <summary>
/// Covers <see cref="DashboardSection{T}"/>: a section is either the data a module answered with or
/// the fact that it could not be read, and no operation turns the second into the first.
/// </summary>
public sealed class DashboardSectionTests
{
    /// <summary>A loaded section hands its data to the loaded case.</summary>
    [Fact]
    public void A_loaded_section_hands_its_data_to_the_loaded_case()
    {
        var section = DashboardSection.Loaded<int>(7);

        var answer = section.Match(data =>
        {
            return $"loaded {data}";
        }, () =>
        {
            return "unavailable";
        });

        Assert.Equal(DashboardSectionState.Loaded, section.State);
        Assert.Equal("loaded 7", answer);
    }

    /// <summary>A loaded zero is a loaded section, not an unavailable one.</summary>
    /// <remarks>Zero is the ordinary answer of an attention count, and it must stay an answer.</remarks>
    [Fact]
    public void A_loaded_zero_is_still_loaded()
    {
        var section = DashboardSection.Loaded<int>(0);

        Assert.Equal(DashboardSectionState.Loaded, section.State);
        Assert.Equal(0, section.Match(data =>
        {
            return data;
        }, () =>
        {
            return -1;
        }));
    }

    /// <summary>An unavailable section takes the unavailable case and never the loaded one.</summary>
    [Fact]
    public void An_unavailable_section_takes_the_unavailable_case()
    {
        var section = DashboardSection.Unavailable<int>();

        var answer = section.Match(data =>
        {
            return $"loaded {data}";
        }, () =>
        {
            return "unavailable";
        });

        Assert.Equal(DashboardSectionState.Unavailable, section.State);
        Assert.Equal("unavailable", answer);
    }

    /// <summary>Switch acts on the case that applies and only on it.</summary>
    [Fact]
    public void Switch_acts_on_the_case_that_applies()
    {
        var seen = new List<string>();

        DashboardSection.Loaded<int>(3).Switch(data =>
        {
            seen.Add($"loaded {data}");
        }, () =>
        {
            seen.Add("unavailable");
        });
        DashboardSection.Unavailable<int>().Switch(data =>
        {
            seen.Add($"loaded {data}");
        }, () =>
        {
            seen.Add("unavailable");
        });

        Assert.Equal(["loaded 3", "unavailable"], seen);
    }

    /// <summary>Mapping a loaded listing counts what it holds.</summary>
    [Fact]
    public void Mapping_a_loaded_listing_derives_a_loaded_count()
    {
        IReadOnlyList<string> tasks = ["failed", "succeeded", "failed"];
        var section = DashboardSection.Loaded<IReadOnlyList<string>>(tasks);

        var failed = section.Map(list =>
        {
            return list.Count(task =>
            {
                return task == "failed";
            });
        });

        Assert.Equal(DashboardSectionState.Loaded, failed.State);
        Assert.Equal(2, failed.Match(data =>
        {
            return data;
        }, () =>
        {
            return -1;
        }));
    }

    /// <summary>
    /// Mapping a listing that could not be read stays unavailable, and the transform never runs —
    /// the failure is not counted as zero things to attend to (issue #248).
    /// </summary>
    [Fact]
    public void Mapping_an_unavailable_listing_stays_unavailable_and_does_not_count_zero()
    {
        var transformed = false;

        var failed = DashboardSection.Unavailable<IReadOnlyList<string>>().Map(list =>
        {
            transformed = true;
            return list.Count;
        });

        Assert.Equal(DashboardSectionState.Unavailable, failed.State);
        Assert.False(transformed);
    }
}
