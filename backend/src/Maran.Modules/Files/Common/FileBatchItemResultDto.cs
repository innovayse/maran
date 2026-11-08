namespace Maran.Modules.Files.Common;

/// <summary>The outcome of one item of a batch; a failure of one item does not stop the others.</summary>
/// <param name="Subject">The path (or trash id) the item named.</param>
/// <param name="Succeeded">Whether the operation was done for this item.</param>
/// <param name="Code">The machine-stable outcome code, for behaviour only; null on success.</param>
/// <param name="Message">The reason in the caller's language, resolved by the server; null on success.</param>
public sealed record FileBatchItemResultDto(string Subject, bool Succeeded, string? Code, string? Message);
