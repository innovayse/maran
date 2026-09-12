namespace Maran.ArchitectureTests;

/// <summary>
/// One module's name as three languages hold it, read from the authored resource files.
/// </summary>
/// <param name="ModuleId">The module's id, so a failure names the module and not only a key.</param>
/// <param name="Key">The manifest's <c>DisplayNameKey</c>.</param>
/// <param name="English">The neutral value, or <c>null</c> when no file declares the key.</param>
/// <param name="Russian">The <c>.ru</c> value, or <c>null</c> when that file declares no entry.</param>
/// <param name="Armenian">The <c>.hy</c> value, or <c>null</c> when that file declares no entry.</param>
/// <remarks>
/// A null is kept distinct from an empty string on purpose: "no entry at all" and "an entry holding
/// nothing" are different mistakes and read differently in a failure message, and the resolver
/// answers them differently too — a missing translated entry falls back to the English text, which
/// is the silent half this row exists to expose.
/// </remarks>
public sealed record ModuleDisplayName(
    string ModuleId,
    string Key,
    string? English,
    string? Russian,
    string? Armenian);
