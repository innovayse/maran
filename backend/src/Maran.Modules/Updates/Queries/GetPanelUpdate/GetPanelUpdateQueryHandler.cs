using Maran.Modules.Updates.Common;
using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Updates.Queries.GetPanelUpdate;

/// <summary>
/// Handles <see cref="GetPanelUpdateQuery"/>: composes the running version with the facts root has
/// recorded (issue #382).
/// </summary>
/// <remarks>
/// <b>An available release is one that is newer than the running one.</b> The check record says what the
/// last manifest named; if the panel has since been updated, or the manifest named an older release, the
/// record says nothing the screen should offer, so <c>available</c> is null rather than a version the
/// button would install over itself.
/// </remarks>
public sealed class GetPanelUpdateQueryHandler
{
    /// <summary>Reads the files root maintains.</summary>
    private readonly PanelUpdateStateReader _reader;

    /// <summary>The version of the running panel.</summary>
    private readonly IPanelVersion _panelVersion;

    /// <summary>Creates the handler.</summary>
    /// <param name="reader">Reads the files root maintains.</param>
    /// <param name="panelVersion">The version of the running panel.</param>
    public GetPanelUpdateQueryHandler(PanelUpdateStateReader reader, IPanelVersion panelVersion)
    {
        _reader = reader;
        _panelVersion = panelVersion;
    }

    /// <summary>Reads the state.</summary>
    /// <param name="query">The query; it has no input.</param>
    /// <param name="cancellationToken">Unused; the reads are small local files.</param>
    /// <returns>The state, always: an unreadable file is "nothing known", never a failure.</returns>
    public Task<Result<PanelUpdateDto>> HandleAsync(GetPanelUpdateQuery query, CancellationToken cancellationToken)
    {
        var current = _panelVersion.Current;
        var facts = _reader.Read();

        PanelUpdateAvailableDto? available = null;
        if (facts.Check is { } check && IsNewer(check.Version, current))
        {
            available = new PanelUpdateAvailableDto(check.Version, check.ReleasedAt, check.NotesUrl);
        }

        var dto = new PanelUpdateDto(
            current,
            facts.Channel,
            available,
            facts.Check?.CheckedAt,
            facts.PreviousVersion,
            PanelUpdateLedgerPolicy.IsInProgress(facts.Signals),
            PanelUpdateLedgerPolicy.IsCheckInProgress(facts.CheckMarkerAge),
            facts.CanStart);

        return Task.FromResult(Result<PanelUpdateDto>.Ok(dto));
    }

    /// <summary>Whether one version is strictly newer than another by Semantic Versioning precedence.</summary>
    /// <param name="candidate">The version the check found.</param>
    /// <param name="running">The version that runs.</param>
    /// <returns>True when both parse and the candidate is greater.</returns>
    private static bool IsNewer(string candidate, string running)
    {
        return PanelUpdateValueRules.TryVersion(candidate, out var newer)
            && SemanticVersion.TryParse(running, out var older)
            && SemanticVersion.Compare(newer!, older!) > 0;
    }
}
