using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Common;

/// <summary>Outward view of one backup destination: everything the settings screen shows.</summary>
/// <remarks>
/// It carries no credential and no secret hint, because no destination on this build holds one — the
/// remote arm is describable and refused, so there is nothing stored that a response could leak.
/// <para>
/// <see cref="Path"/> is nullable for a reason a reader should not have to guess: the panel does not
/// own a local destination's directory and cannot always learn it, and "unknown" and "the default"
/// are different answers that an empty string would merge into one.
/// </para>
/// </remarks>
/// <param name="Id">The destination's identity, which a schedule may name.</param>
/// <param name="Name">
/// The label as the row stores it. Machine-stable and never translated: it is what an operator sees
/// in <c>psql</c>, what a support ticket names, and — for a destination an operator saved — the
/// words they typed.
/// </param>
/// <param name="DisplayName">
/// The same label as a screen shows it. It differs from <paramref name="Name"/> for exactly one
/// destination — the one this panel seeded and named itself, which is shown in the caller's
/// language rather than in the English the row holds — and equals it for every destination an
/// operator named. It travels beside <paramref name="Name"/> rather than replacing it so that the
/// stable value stays available to every reader that is not a heading.
/// </param>
/// <param name="Kind">Which kind of storage it names.</param>
/// <param name="Path">
/// Where this destination's artifacts rest, established rather than restated: for a local
/// destination it is the directory the AGENT reported on this request's handshake. It is
/// <c>null</c> when the panel could not establish it — the agent was unreachable, or is older than
/// the field — and a screen must then say the path is not known rather than showing a default. A
/// plausible default shown as fact is precisely the defect this shape closes.
/// </param>
/// <param name="IsDefault">Whether backups naming no destination are written here.</param>
/// <param name="CreatedAt">When the panel first recorded it.</param>
public sealed record BackupDestinationDto(
    Guid Id,
    string Name,
    string DisplayName,
    BackupDestinationKind Kind,
    string? Path,
    bool IsDefault,
    DateTimeOffset CreatedAt);
