using System.Text;
using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Queries.ExportMigrationSummary;

/// <summary>
/// Handles <see cref="ExportMigrationSummaryQuery"/>: one row per account, as CSV or JSON (issue #197, phase 2).
/// </summary>
/// <remarks>
/// A value a source chose (an account name, a domain, an owner) can start with a character a spreadsheet reads as a
/// formula, so every CSV field goes through <see cref="CsvField"/>, which neutralises it. The file carries no key, no
/// grant and no generated password.
/// </remarks>
public sealed class ExportMigrationSummaryQueryHandler
{
    /// <summary>The CSV header.</summary>
    private static readonly string[] Header =
    [
        "account", "primary_domain", "state", "failure_code", "source_bytes", "files_slices", "databases_loaded", "dump_bytes", "synced_at", "cut_over_at",
    ];

    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    public ExportMigrationSummaryQueryHandler(ServerMigrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Builds the file.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file, or <c>MigrationJobNotFound</c>.</returns>
    public async Task<Result<MigrationSummaryFileDto>> HandleAsync(ExportMigrationSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var job = await _dbContext.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == query.JobId, cancellationToken);
        if (job is null)
        {
            return Result<MigrationSummaryFileDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var accounts = await _dbContext.MigrationJobAccounts.AsNoTracking()
            .Where(row => row.JobId == job.Id)
            .OrderBy(row => row.SourceAccountName)
            .ToListAsync(cancellationToken);
        var rows = accounts.Select(account =>
        {
            return new[]
        {
            account.SourceAccountName,
            account.PrimaryDomain ?? string.Empty,
            account.State.ToString(),
            account.FailureCode ?? string.Empty,
            account.SourceBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            account.SlicesCompleted.ToString(System.Globalization.CultureInfo.InvariantCulture),
            account.DatabasesLoaded.ToString(System.Globalization.CultureInfo.InvariantCulture),
            account.DumpBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            account.SyncedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            account.CutOverAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
        }).ToList();

        if (query.AsJson)
        {
            var objects = rows.Select(row =>
            {
                return Header.Zip(row).ToDictionary(pair =>
            {
                return pair.First;
            }, pair =>
            {
                return pair.Second;
            });
            }).ToList();

            return Result<MigrationSummaryFileDto>.Ok(new MigrationSummaryFileDto(
                "migration-summary.json",
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(objects, JsonSerializerOptions.Web)));
        }

        var csv = new StringBuilder();
        csv.Append(string.Join(',', Header)).Append("\r\n");
        foreach (var row in rows)
        {
            csv.Append(string.Join(',', row.Select(CsvField.Of))).Append("\r\n");
        }

        return Result<MigrationSummaryFileDto>.Ok(
            new MigrationSummaryFileDto("migration-summary.csv", "text/csv", Encoding.UTF8.GetBytes(csv.ToString())));
    }
}
