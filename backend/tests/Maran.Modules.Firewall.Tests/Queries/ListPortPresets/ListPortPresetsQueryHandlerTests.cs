using Maran.Modules.Firewall.Commands.AllowPort;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Queries.ListPortPresets;
using Maran.Modules.Firewall.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Tests.Queries.ListPortPresets;

/// <summary>What the rule screen's preset card is offered: every preset of the catalog, named, with its exact rules.</summary>
public sealed class ListPortPresetsQueryHandlerTests
{
    /// <summary>The card lists exactly the presets the catalog holds, in the catalog's order.</summary>
    [Fact]
    public async Task The_card_lists_exactly_the_presets_of_the_catalog()
    {
        var handler = new ListPortPresetsQueryHandler(RealNames());

        var result = await handler.HandleAsync(new ListPortPresetsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            FirewallPresetCatalog.All.Select(preset =>
            {
                return preset.Key;
            }),
            result.Value.Select(preset =>
            {
                return preset.Key;
            }));
    }

    /// <summary>Every preset is named from the shipped resource files, not by its member name as a fallback.</summary>
    [Fact]
    public async Task Every_preset_is_named_from_the_shipped_resources()
    {
        // Observed against the real resx: a fallback would read "Web" and "MysqlExternal" here.
        var handler = new ListPortPresetsQueryHandler(RealNames());

        var result = await handler.HandleAsync(new ListPortPresetsQuery(), CancellationToken.None);

        Assert.Equal(
            ["Open the web ports", "MySQL reachable from outside"],
            result.Value.Select(preset =>
            {
                return preset.Name;
            }));
    }

    /// <summary>The web preset opens 80 then 443 over TCP to every source, and cannot be switched off from the card.</summary>
    [Fact]
    public async Task The_web_preset_opens_80_then_443_to_every_source_and_is_not_closable()
    {
        var handler = new ListPortPresetsQueryHandler(RealNames());

        var result = await handler.HandleAsync(new ListPortPresetsQuery(), CancellationToken.None);

        var web = result.Value.Single(preset =>
        {
            return preset.Key == FirewallPresetKey.Web;
        });
        Assert.False(web.Closable);
        Assert.Equal(
            [(80, "0.0.0.0/0"), (443, "0.0.0.0/0")],
            web.Rules.Select(rule =>
            {
                return (rule.Port, rule.SourceCidr);
            }));
        Assert.All(web.Rules, rule =>
        {
            Assert.Null(rule.PortTo);
        });
    }

    /// <summary>The MySQL preset opens 3306 to every source and can be switched off again.</summary>
    [Fact]
    public async Task The_mysql_preset_opens_3306_to_every_source_and_is_closable()
    {
        var handler = new ListPortPresetsQueryHandler(RealNames());

        var result = await handler.HandleAsync(new ListPortPresetsQuery(), CancellationToken.None);

        var mysql = result.Value.Single(preset =>
        {
            return preset.Key == FirewallPresetKey.MysqlExternal;
        });
        Assert.True(mysql.Closable);
        var rule = Assert.Single(mysql.Rules);
        Assert.Equal(3306, rule.Port);
        Assert.Equal("0.0.0.0/0", rule.SourceCidr);
    }

    /// <summary>Every rule of every preset passes the allow command's own validator, so a preset can never open what the form would refuse.</summary>
    [Fact]
    public async Task Every_preset_rule_passes_the_allow_command_validator()
    {
        var handler = new ListPortPresetsQueryHandler(RealNames());
        var validator = new AllowPortCommandValidator();

        var result = await handler.HandleAsync(new ListPortPresetsQuery(), CancellationToken.None);

        var rules = result.Value.SelectMany(preset =>
        {
            return preset.Rules;
        }).ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            var verdict = validator.Validate(new AllowPortCommand(rule.Port, rule.Protocol, rule.SourceCidr, rule.PortTo));
            Assert.True(verdict.IsValid);
        });
    }

    /// <summary>Builds the localizer over this module's real display-name resources.</summary>
    /// <returns>The localizer, resolving through the same factory the panel registers.</returns>
    private static StringLocalizer<DisplayNames> RealNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new StringLocalizer<DisplayNames>(factory);
    }
}
