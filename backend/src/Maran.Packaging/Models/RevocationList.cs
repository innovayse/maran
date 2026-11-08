namespace Maran.Packaging.Models;

/// <summary>
/// The entry format of the marketplace's revocation list: which module versions a panel must refuse
/// although their signature is valid. Defined here and not in the Sdk because only verification reads
/// it. Serving it, checking its own signature with a key the panel already pins, and refusing to go
/// back to an older list are the loader's work (issue #184); this type fixes only the shape.
/// </summary>
/// <param name="FormatVersion">The list's format revision.</param>
/// <param name="Sequence">Strictly increasing per issued list; a panel never accepts a lower one than it holds.</param>
/// <param name="IssuedAt">When the list was issued.</param>
/// <param name="Revoked">Every withdrawn range.</param>
public sealed record RevocationList(
    int FormatVersion,
    long Sequence,
    DateTimeOffset IssuedAt,
    IReadOnlyList<RevocationEntry> Revoked);
