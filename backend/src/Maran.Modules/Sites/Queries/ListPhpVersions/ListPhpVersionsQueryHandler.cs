using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;

namespace Maran.Modules.Sites.Queries.ListPhpVersions;

/// <summary>Handles <see cref="ListPhpVersionsQuery"/> by asking the agent what the host has installed.</summary>
/// <remarks>
/// Host-level reference data, not tenant data: multi-PHP is installed once per server and then
/// bound to any number of sites, so there is nothing here to scope to an account. The agent's
/// answer is reshaped into the module's own <see cref="PhpVersionDto"/>, which drops the FPM socket
/// directory — a filesystem path has no business on a customer's screen (rules/security.md).
/// </remarks>
public sealed class ListPhpVersionsQueryHandler
{
    /// <summary>The agent client listing the host's installed PHP runtimes.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Creates the handler.</summary>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    public ListPhpVersionsQueryHandler(IAgentPhpClient php)
    {
        _php = php;
    }

    /// <summary>Returns the installed versions, or the agent's own typed failure.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The installed versions, or the agent's typed error.</returns>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> HandleAsync(
        ListPhpVersionsQuery query,
        CancellationToken cancellationToken)
    {
        var versions = await _php.ListVersionsAsync(cancellationToken);
        if (!versions.IsSuccess)
        {
            return Result<IReadOnlyList<PhpVersionDto>>.Fail(versions.Error!);
        }

        IReadOnlyList<PhpVersionDto> installed = versions.Value
            .Select(version =>
            {
                return new PhpVersionDto(version.Version, version.IsDefault);
            })
            .ToList();

        return Result<IReadOnlyList<PhpVersionDto>>.Ok(installed);
    }
}
