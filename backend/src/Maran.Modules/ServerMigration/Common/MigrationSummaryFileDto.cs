namespace Maran.Modules.ServerMigration.Common;

/// <summary>The summary of a job as a file the administrator downloads.</summary>
/// <param name="FileName">The suggested file name.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="Content">The bytes.</param>
public sealed record MigrationSummaryFileDto(string FileName, string ContentType, byte[] Content);
