using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Services;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Sites.Queries.ListPhpExtensions;

/// <summary>Handles <see cref="ListPhpExtensionsQuery"/> by asking the agent what one version loads.</summary>
/// <remarks>
/// <para>
/// Host-level reference data, not tenant data, like the version list it hangs off. Everyone who may
/// see the versions may see which extensions each loads, because a customer choosing a version needs
/// to know whether it has what their code requires.
/// </para>
/// <para>
/// <b>What a customer is not told.</b> The availability — whether this host's OS family can install or
/// switch the extension — is how the host's packages are arranged and the only thing a switch needs;
/// it goes to an administrator, with its word, and is left <see langword="null"/> for everyone else. No package name
/// leaves the agent at all.
/// </para>
/// </remarks>
public sealed class ListPhpExtensionsQueryHandler
{
    /// <summary>The agent client listing a version's extensions.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>The caller, to decide whether the availability is theirs to see.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Resolves the localized extension labels.</summary>
    private readonly SiteDisplayNames _displayNames;

    /// <summary>Reads which PHP versions are installed as apps.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Creates the handler.</summary>
    /// <param name="php">The agent client listing a version's extensions.</param>
    /// <param name="currentUser">The caller, to decide whether the availability is theirs to see.</param>
    /// <param name="displayNames">Resolves the localized extension labels.</param>
    /// <param name="stack">Reads which PHP versions are installed as apps.</param>
    public ListPhpExtensionsQueryHandler(
        IAgentPhpClient php,
        ICurrentUser currentUser,
        SiteDisplayNames displayNames,
        SiteStackGate stack)
    {
        _stack = stack;
        _php = php;
        _currentUser = currentUser;
        _displayNames = displayNames;
    }

    /// <summary>Returns the version's catalogue extensions, or the agent's own typed failure.</summary>
    /// <param name="query">The version to read.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One entry per catalogue extension in the agent's order, or the agent's typed error.</returns>
    public async Task<Result<IReadOnlyList<PhpExtensionDto>>> HandleAsync(
        ListPhpExtensionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // PHP is an app like any other: with none installed there is no PHP to report on, and the answer
        // says so rather than listing an empty or stale host. Enforced here, not in the SPA, which only
        // hides the menu (docs/apps/README.md).
        var installed = await _stack.ReadAsync(cancellationToken);
        if (!installed.HasPhp(query.Version))
        {
            return Result<IReadOnlyList<PhpExtensionDto>>.Fail(SiteHostApps.NotInstalledFailure());
        }

        var extensions = await _php.ListExtensionsAsync(query.Version, cancellationToken);
        if (!extensions.IsSuccess)
        {
            return Result<IReadOnlyList<PhpExtensionDto>>.Fail(extensions.Error!);
        }

        var isAdministrator = _currentUser.Caller is AdministratorCaller;

        // A name the panel's copy of the catalogue does not hold is dropped rather than labelled: it
        // would have no label, and a button for it would be refused by the toggle's own validator.
        IReadOnlyList<PhpExtensionDto> listed = extensions.Value
            .Where(extension =>
            {
                return PhpExtensionCatalogue.IsKnown(extension.Name);
            })
            .Select(extension =>
            {
                var label = _displayNames.OfPhpExtension(extension.Name);

                return isAdministrator
                    ? PhpExtensionMapper.ForAdministrator(extension, label, availability => { return _displayNames.Of(availability); })
                    : PhpExtensionMapper.ForCustomer(extension, label);
            })
            .ToList();

        return Result<IReadOnlyList<PhpExtensionDto>>.Ok(listed);
    }
}
