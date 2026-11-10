namespace Maran.Modules.ServerMigration.Common;

/// <summary>The DNS helper's answer for a job (decision D19): where each domain of the job resolves, from this server.</summary>
/// <param name="Rows">One row per domain.</param>
/// <param name="ThisServerAddresses">The addresses this server holds, which a domain should resolve to after the cut-over.</param>
public sealed record MigrationDnsAdviceDto(IReadOnlyList<MigrationDnsRowDto> Rows, IReadOnlyList<string> ThisServerAddresses);
