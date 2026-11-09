using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Simulated.Limits;

namespace Maran.Agent.Simulated.Validation;

/// <summary>The agent's second look at php.ini settings: bounded plain text, in bounded numbers.</summary>
public static class SimulatedPhpSettingRules
{
    /// <summary>Whether the list is within its cap and every entry is plain, bounded text.</summary>
    /// <param name="limits">The caps in force.</param>
    /// <param name="settings">The settings a request named.</param>
    /// <returns>True when the list is acceptable.</returns>
    public static bool AreWellFormed(SimulatedAgentLimits limits, IReadOnlyList<PhpSettingDto> settings)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Count <= limits.MaxSettingOverridesPerSite
            && settings.All(
                setting =>
                {
                    return setting.Name.Length > 0
                        && SimulatedInput.IsPlainText(setting.Name, limits.MaxSettingTextLength)
                        && SimulatedInput.IsPlainText(setting.Value, limits.MaxSettingTextLength);
                });
    }
}
