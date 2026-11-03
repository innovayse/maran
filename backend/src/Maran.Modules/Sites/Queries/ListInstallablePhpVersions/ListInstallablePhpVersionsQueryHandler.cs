using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Mappers;

namespace Maran.Modules.Sites.Queries.ListInstallablePhpVersions;

/// <summary>Handles <see cref="ListInstallablePhpVersionsQuery"/> by asking the agent what the host can install.</summary>
/// <remarks>
/// Host-level reference data, not tenant data, like the installed list beside it. The answer is the
/// intersection of the panel's supported set and what the host's package repository publishes, decided
/// by the agent; the install command is validated against this same set, so the list never offers a
/// version the install would refuse.
/// </remarks>
public sealed class ListInstallablePhpVersionsQueryHandler
{
    /// <summary>The agent client listing the versions the host can install.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Creates the handler.</summary>
    /// <param name="php">The agent client listing the versions the host can install.</param>
    public ListInstallablePhpVersionsQueryHandler(IAgentPhpClient php)
    {
        _php = php;
    }

    /// <summary>Returns the installable versions, or the agent's own typed failure.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The installable versions, newest first, or the agent's typed error.</returns>
    public async Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> HandleAsync(
        ListInstallablePhpVersionsQuery query,
        CancellationToken cancellationToken)
    {
        var versions = await _php.ListInstallableVersionsAsync(cancellationToken);
        if (!versions.IsSuccess)
        {
            return Result<IReadOnlyList<InstallablePhpVersionDto>>.Fail(versions.Error!);
        }

        return Result<IReadOnlyList<InstallablePhpVersionDto>>.Ok(InstallablePhpVersionMapper.From(versions.Value));
    }
}
