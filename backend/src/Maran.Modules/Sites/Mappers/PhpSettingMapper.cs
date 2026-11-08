using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Mappers;

/// <summary>Restates the settings a pool carries in the shape the agent client sends.</summary>
/// <remarks>It translates and never decides: the clamp has already been applied to what it is given.</remarks>
public static class PhpSettingMapper
{
    /// <summary>Restates a list of settings for the agent.</summary>
    /// <param name="settings">The settings a pool carries, already clamped to the plan.</param>
    /// <returns>The same settings as the agent client's DTOs, in the same order.</returns>
    public static IReadOnlyList<PhpSettingDto> ToAgent(IEnumerable<PhpSettingValue> settings)
    {
        return settings
            .Select(setting =>
            {
                return new PhpSettingDto(setting.Name, setting.Value);
            })
            .ToList();
    }
}
