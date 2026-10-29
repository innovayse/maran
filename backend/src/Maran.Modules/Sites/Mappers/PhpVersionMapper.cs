using Maran.Modules.Sites.Common;
using AgentPhpVersionDto = Maran.Agent.Client.Services.PhpService.PhpVersionDto;

namespace Maran.Modules.Sites.Mappers;

/// <summary>
/// Restates the agent client's view of installed PHP runtimes as the panel's outward
/// <see cref="PhpVersionDto"/>.
/// </summary>
/// <remarks>
/// Two records share the name <c>PhpVersionDto</c> — the agent client's and this module's — and a
/// handler that returned the wrong one still compiled, because the bus is asked for a result type by
/// the caller and never checked against the handler. The install endpoint answered a 500 for that
/// reason even when the install succeeded (issue #99). With one conversion used by every handler that
/// answers with PHP versions, there is one place where the agent's FPM socket directory — a
/// filesystem path, operator-facing detail — is dropped (rules/security.md).
/// </remarks>
public static class PhpVersionMapper
{
    /// <summary>Restates the agent's installed versions as the panel's outward view.</summary>
    /// <param name="versions">The agent client's versions.</param>
    /// <returns>The same versions, without the FPM socket directory.</returns>
    public static IReadOnlyList<PhpVersionDto> From(IEnumerable<AgentPhpVersionDto> versions)
    {
        return versions
            .Select(version =>
            {
                return new PhpVersionDto(version.Version, version.IsDefault);
            })
            .ToList();
    }
}
