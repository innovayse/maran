using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Firewall.Queries.ListProtocolChoices;

/// <summary>
/// Handles <see cref="ListProtocolChoicesQuery"/> by naming every member of the agent contract's
/// protocol enum for the request's culture.
/// </summary>
/// <remarks>
/// The answer is the enum itself, in the order of its values, so it cannot disagree with what
/// <c>AllowPortCommandValidator</c> accepts: both read the same type. A name is looked up as
/// <c>FirewallProtocol</c> plus the member name; <see cref="IStringLocalizer"/> answers a missing key
/// with the key, so the miss is detected and the member name is shown instead, which keeps a protocol
/// added to the contract selectable before anyone has translated it.
/// </remarks>
public sealed class ListProtocolChoicesQueryHandler
{
    /// <summary>The prefix a protocol's resx key carries, so protocols cannot collide with other names.</summary>
    private const string KeyPrefix = "FirewallProtocol";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public ListProtocolChoicesQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every protocol a rule may name, named for the caller.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<FirewallProtocolChoiceDto>>> HandleAsync(
        ListProtocolChoicesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FirewallProtocolChoiceDto> choices = Enum.GetValues<AgentFirewallProtocol>()
            .Order()
            .Select(protocol =>
            {
                var memberName = protocol.ToString();
                var localized = _displayNames[KeyPrefix + memberName];

                var name = localized.ResourceNotFound ? memberName : localized.Value;

                return new FirewallProtocolChoiceDto(protocol, name);
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<FirewallProtocolChoiceDto>>.Ok(choices));
    }
}
