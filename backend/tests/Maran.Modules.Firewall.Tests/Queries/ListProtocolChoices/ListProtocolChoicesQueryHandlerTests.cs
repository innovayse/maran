using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Queries.ListProtocolChoices;
using Maran.Modules.Firewall.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Tests.Queries.ListProtocolChoices;

/// <summary>What the rule form's protocol picker is offered: every protocol the agent contract knows, named.</summary>
public sealed class ListProtocolChoicesQueryHandlerTests
{
    /// <summary>The picker lists exactly the members of the protocol enum, so it cannot offer one the command refuses.</summary>
    [Fact]
    public async Task The_picker_lists_exactly_the_protocols_the_agent_contract_knows()
    {
        var handler = new ListProtocolChoicesQueryHandler(RealNames());

        var result = await handler.HandleAsync(new ListProtocolChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Enum.GetValues<AgentFirewallProtocol>().Order(),
            result.Value.Select(choice =>
            {
                return choice.Protocol;
            }));
    }

    /// <summary>Every protocol carries a name read from the shipped resource files, not its member name by fallback.</summary>
    [Fact]
    public async Task Every_protocol_is_named_from_the_shipped_resources()
    {
        // Observed against the real resx: a fallback to the member name would read "Tcp" here, which
        // is the one spelling the shipped table must never leave on screen.
        var handler = new ListProtocolChoicesQueryHandler(RealNames());

        var result = await handler.HandleAsync(new ListProtocolChoicesQuery(), CancellationToken.None);

        Assert.Equal(
            ["TCP", "UDP"],
            result.Value.Select(choice =>
            {
                return choice.ProtocolName;
            }));
    }

    /// <summary>Builds the localizer over this module's real display-name resources.</summary>
    /// <returns>The localizer, resolving through the same factory the panel registers.</returns>
    private static IStringLocalizer<DisplayNames> RealNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new StringLocalizer<DisplayNames>(factory);
    }
}
