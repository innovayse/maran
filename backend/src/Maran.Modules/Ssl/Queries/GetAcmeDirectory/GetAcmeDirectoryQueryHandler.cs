using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Policies;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Queries.GetAcmeDirectory;

/// <summary>Handles <see cref="GetAcmeDirectoryQuery"/>.</summary>
/// <remarks>
/// Restates configuration and decides nothing: the kind is <see cref="AcmeDirectoryKindPolicy"/>'s
/// and the words for it are the Ssl display names, resolved in the caller's culture.
/// </remarks>
public sealed class GetAcmeDirectoryQueryHandler
{
    /// <summary>The ACME settings the panel booted with.</summary>
    private readonly IOptions<AcmeOptions> _options;

    /// <summary>The Ssl module's display names.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="options">The ACME settings.</param>
    /// <param name="displayNames">The Ssl module's display names.</param>
    public GetAcmeDirectoryQueryHandler(IOptions<AcmeOptions> options, IStringLocalizer<DisplayNames> displayNames)
    {
        _options = options;
        _displayNames = displayNames;
    }

    /// <summary>Describes the configured authority.</summary>
    /// <param name="query">The query, which carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The directory URL, its host, its kind and the kind's name in the caller's language.</returns>
    public Task<AcmeDirectoryDto> HandleAsync(GetAcmeDirectoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var url = _options.Value.DirectoryUrl;
        var kind = AcmeDirectoryKindPolicy.KindOf(url);
        var host = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : string.Empty;

        return Task.FromResult(new AcmeDirectoryDto(url, host, kind, _displayNames[KindNameKey(kind)].Value));
    }

    /// <summary>Maps a kind to the display-name resource that names it.</summary>
    /// <param name="kind">A kind <see cref="AcmeDirectoryKindPolicy"/> answered.</param>
    /// <returns>The resource key.</returns>
    private static string KindNameKey(string kind)
    {
        if (string.Equals(kind, AcmeDirectoryKindPolicy.Production, StringComparison.Ordinal))
        {
            return "AcmeDirectoryKindProduction";
        }

        if (string.Equals(kind, AcmeDirectoryKindPolicy.Staging, StringComparison.Ordinal))
        {
            return "AcmeDirectoryKindStaging";
        }

        return "AcmeDirectoryKindCustom";
    }
}
