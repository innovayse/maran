using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Queries.GetMigrationAccountReport;

/// <summary>Handles <see cref="GetMigrationAccountReportQuery"/>.</summary>
public sealed class GetMigrationAccountReportQueryHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    public GetMigrationAccountReportQueryHandler(ServerMigrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the report.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report; <c>MigrationJobAccountNotFound</c> when the account does not exist or has none yet.</returns>
    public async Task<Result<MigrationAccountReportDto>> HandleAsync(
        GetMigrationAccountReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await _dbContext.MigrationJobAccounts.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == query.AccountId && row.JobId == query.JobId, cancellationToken);
        var report = account?.ReportJson is null
            ? null
            : JsonSerializer.Deserialize<MigrationAccountReport>(account.ReportJson, JsonSerializerOptions.Web);
        if (account is null || report is null)
        {
            return Result<MigrationAccountReportDto>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationJobAccountNotFound), ErrorType.NotFound));
        }

        return Result<MigrationAccountReportDto>.Ok(new MigrationAccountReportDto(
            account.SourceAccountName,
            report.SourceBytes,
            report.Slices,
            report.FilesConverged,
            report.Databases.Select(row =>
            {
                return new MigrationReportDatabaseDto(row.Name, row.DumpBytes);
            }).ToList(),
            report.Sites.Select(row =>
            {
                return new MigrationReportSiteDto(row.Domain, row.StatusCode, row.Title, row.Serves);
            }).ToList(),
            account.CutOverAt));
    }
}
