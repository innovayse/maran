using Maran.Modules.Apps.Persistence.Configurations;
using Maran.Modules.Apps.Services;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Apps.Tests.Services;

/// <summary>What a refused ledger save means: only a lost race is a conflict, anything else is a fault (issue #526).</summary>
public sealed class AppLedgerConflictsTests
{
    /// <summary>Builds the database failure PostgreSQL raises for a duplicate key on an index.</summary>
    /// <param name="constraint">The index that refused the row.</param>
    /// <returns>A <see cref="PostgresException"/> carrying SqlState <c>23505</c> and the index name.</returns>
    public static PostgresException UniqueViolation(string constraint)
    {
        return new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraint);
    }

    /// <summary>A row changed since it was read is the operation-in-progress conflict.</summary>
    [Fact]
    public void A_changed_row_is_an_operation_in_progress_conflict()
    {
        var error = AppLedgerConflicts.Classify(new DbUpdateConcurrencyException("changed"));

        Assert.Equal("AppsOperationInProgress", error!.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    /// <summary>A second row for the same app is the operation-in-progress conflict.</summary>
    [Fact]
    public void A_second_row_for_an_app_is_an_operation_in_progress_conflict()
    {
        var error = AppLedgerConflicts.Classify(
            new DbUpdateException("refused", UniqueViolation(InstalledAppConfiguration.AppIdIndexName)));

        Assert.Equal("AppsOperationInProgress", error!.Code);
    }

    /// <summary>A second active web server is the web server conflict.</summary>
    [Fact]
    public void A_second_active_web_server_is_a_web_server_conflict()
    {
        var error = AppLedgerConflicts.Classify(
            new DbUpdateException("refused", UniqueViolation(InstalledAppConfiguration.ActiveWebServerIndexName)));

        Assert.Equal("AppsWebServerAlreadyInstalled", error!.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    /// <summary>The control: a failure that is not a unique violation is not a conflict.</summary>
    [Fact]
    public void Any_other_failure_is_not_a_conflict()
    {
        var notUnique = new PostgresException("boom", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);

        Assert.Null(AppLedgerConflicts.Classify(new DbUpdateException("boom", notUnique)));
        Assert.Null(AppLedgerConflicts.Classify(new DbUpdateException("boom", new InvalidOperationException("boom"))));
        Assert.Null(AppLedgerConflicts.Classify(new DbUpdateException("boom")));
    }
}
