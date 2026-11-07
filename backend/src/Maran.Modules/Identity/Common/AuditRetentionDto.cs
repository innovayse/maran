namespace Maran.Modules.Identity.Common;

/// <summary>How long the audit journal is kept, as the settings screen reads it.</summary>
/// <param name="RetentionDays">Days a journal row is kept; <c>null</c> means it is kept indefinitely.</param>
/// <param name="MinimumDays">The shortest window the panel accepts, so the screen needs no constant of its own.</param>
/// <param name="MaximumDays">The longest window the panel accepts.</param>
/// <param name="LastRunAt">When the daily maintenance run last finished; <c>null</c> if it never has.</param>
public sealed record AuditRetentionDto(int? RetentionDays, int MinimumDays, int MaximumDays, DateTimeOffset? LastRunAt);
