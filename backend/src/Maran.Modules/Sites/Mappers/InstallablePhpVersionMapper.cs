using AgentInstallablePhpVersionDto = Maran.Agent.Client.Services.PhpService.InstallablePhpVersionDto;
using InstallablePhpVersionDto = Maran.Modules.Sites.Common.InstallablePhpVersionDto;

namespace Maran.Modules.Sites.Mappers;

/// <summary>
/// Restates the agent client's installable PHP versions as the panel's outward
/// <see cref="InstallablePhpVersionDto"/>.
/// </summary>
/// <remarks>
/// Kept apart from the installed-version mapper because the two answer different questions: that one
/// restates what the host has, this one what the host could have. The shapes are alike and the DTO
/// names are shared with the agent client's, which is why the aliases above exist.
/// </remarks>
public static class InstallablePhpVersionMapper
{
    /// <summary>Restates the agent's installable versions as the panel's outward view.</summary>
    /// <param name="versions">The agent client's installable versions.</param>
    /// <returns>The same versions in the same order, each with its installed flag.</returns>
    public static IReadOnlyList<InstallablePhpVersionDto> From(IEnumerable<AgentInstallablePhpVersionDto> versions)
    {
        return versions
            .Select(version =>
            {
                return new InstallablePhpVersionDto(version.Version, version.Installed);
            })
            .ToList();
    }
}
