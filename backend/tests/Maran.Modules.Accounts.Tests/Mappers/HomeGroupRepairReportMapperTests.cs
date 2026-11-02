using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AgentHomeGroupRepairReport = Maran.Agent.Client.Services.AccountsService.HomeGroupRepairReportDto;
using AgentRefusedHome = Maran.Agent.Client.Services.AccountsService.RefusedHomeDto;
using AgentRepairedHome = Maran.Agent.Client.Services.AccountsService.RepairedHomeDto;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioural contract of <see cref="HomeGroupRepairReportMapper"/>: the kind of pass is the method
/// the caller chose, so a report-only pass and a repairing pass over the same census are different
/// shapes, each carrying only the list its pass fills.
/// </summary>
public sealed class HomeGroupRepairReportMapperTests
{
    /// <summary>A report-only census becomes an inspection carrying the homes a repair would re-group.</summary>
    [Fact]
    public void A_report_only_census_becomes_an_inspection_carrying_the_homes_a_repair_would_regroup()
    {
        var census = new AgentHomeGroupRepairReport(
            3, 1, [], [new AgentRepairedHome("alice", "/home/alice")], [new AgentRefusedHome("carol", "/home/carol", "OwnerMismatch")]);

        var dto = HomeGroupRepairReportMapper.ToInspection(census, RefusalText());

        Assert.Equal(3u, dto.Examined);
        Assert.Equal(1u, dto.AlreadyCorrect);
        Assert.Equal("alice", Assert.Single(dto.WouldRepair).AccountUsername);
        Assert.Equal("carol", Assert.Single(dto.Refused).AccountUsername);
    }

    /// <summary>A repairing census becomes a repair carrying the homes that were re-grouped.</summary>
    [Fact]
    public void A_repairing_census_becomes_a_repair_carrying_the_homes_that_were_regrouped()
    {
        var census = new AgentHomeGroupRepairReport(
            2, 1, [new AgentRepairedHome("bob", "/home/bob")], [], []);

        var dto = HomeGroupRepairReportMapper.ToRepaired(census, RefusalText());

        Assert.Equal("bob", Assert.Single(dto.Repaired).AccountUsername);
        Assert.Empty(dto.Refused);
    }

    /// <summary>The two passes over an empty census stay different shapes: the case is not read off the lists.</summary>
    [Fact]
    public void The_two_passes_over_an_empty_census_stay_different_shapes()
    {
        var census = new AgentHomeGroupRepairReport(9, 9, [], [], []);

        HomeGroupRepairReportDto inspected = HomeGroupRepairReportMapper.ToInspection(census, RefusalText());
        HomeGroupRepairReportDto repaired = HomeGroupRepairReportMapper.ToRepaired(census, RefusalText());

        Assert.IsType<HomeGroupInspectionReportDto>(inspected);
        Assert.IsType<HomeGroupRepairedReportDto>(repaired);
    }

    /// <summary>Builds a real localizer over this module's own embedded resources.</summary>
    /// <returns>The refusal wording resolver.</returns>
    private static HomeGroupRepairRefusalDisplayNames RefusalText()
    {
        return new HomeGroupRepairRefusalDisplayNames(new StringLocalizer<DisplayNames>(
            new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance)));
    }
}
