using System.Text.Json;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// The process ceiling a plan choice carries (issue #194): the new-account form starts its own process
/// field from it, in the one shape every plan ceiling travels in, a figure only beside a cap.
/// </summary>
public sealed class PlanChoiceProcessCeilingTests
{
    /// <summary>A plan that caps processes is offered with the cap.</summary>
    [Fact]
    public void A_plan_that_caps_processes_is_offered_with_the_cap()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 1_024, 5, 2, 3, 0, 5, 5);
        plan.ChangeRuntimeLimits(RuntimeLimits.Of(
            ResourceLimit.Unlimited(), ResourceLimit.Unlimited(), ResourceLimit.Limited(64)));

        var choice = PlanMapper.ToChoiceDto(plan, new StubDisplayNames());

        Assert.Equal(new LimitedResourceCeilingDto(64), choice.MaxProcesses);
    }

    /// <summary>A plan that states no process ceiling is offered as unlimited.</summary>
    [Fact]
    public void A_plan_that_states_no_process_ceiling_is_offered_as_unlimited()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Free", 1_024, 5, 2, 3, 0, 5, 5);

        var choice = PlanMapper.ToChoiceDto(plan, new StubDisplayNames());

        Assert.Equal(new UnlimitedResourceCeilingDto(), choice.MaxProcesses);
    }

    /// <summary>On the wire the ceiling reads <c>{kind: limited, maximum}</c> or <c>{kind: unlimited}</c>, as the form expects.</summary>
    [Fact]
    public void On_the_wire_the_ceiling_reads_kind_and_maximum()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var limited = new PlanChoiceDto(Guid.NewGuid(), "Agency", 1_024, 5, new LimitedResourceCeilingDto(64));
        var unlimited = limited with { MaxProcesses = new UnlimitedResourceCeilingDto() };

        using var limitedJson = JsonDocument.Parse(JsonSerializer.Serialize(limited, options));
        using var unlimitedJson = JsonDocument.Parse(JsonSerializer.Serialize(unlimited, options));

        var cap = limitedJson.RootElement.GetProperty("maxProcesses");
        Assert.Equal("limited", cap.GetProperty("kind").GetString());
        Assert.Equal(64, cap.GetProperty("maximum").GetInt32());
        var none = unlimitedJson.RootElement.GetProperty("maxProcesses");
        Assert.Equal("unlimited", none.GetProperty("kind").GetString());
        Assert.False(none.TryGetProperty("maximum", out _));
    }
}
