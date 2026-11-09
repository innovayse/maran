namespace Maran.Modules.Files.Common;

/// <summary>What every batch route answers: one outcome per requested item, in the order requested.</summary>
/// <param name="Items">The outcomes.</param>
public sealed record FileBatchResultDto(IReadOnlyList<FileBatchItemResultDto> Items);
