using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.ServerMigration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationJobs : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationJobs",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Concurrency = table.Column<int>(type: "integer", nullable: false),
                    BandwidthLimitKib = table.Column<int>(type: "integer", nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ExpectedKeyFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationJobs_RemoteServers_RemoteServerId",
                        column: x => x.RemoteServerId,
                        principalSchema: "servermigration",
                        principalTable: "RemoteServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MigrationJobAccounts",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceHome = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    SourceDiskBytes = table.Column<long>(type: "bigint", nullable: true),
                    CronEntryCount = table.Column<int>(type: "integer", nullable: false),
                    MailboxCount = table.Column<int>(type: "integer", nullable: false),
                    InventoryJson = table.Column<string>(type: "text", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetResellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    OwnerLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PrimaryDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    PullGrant = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provisioned = table.Column<bool>(type: "boolean", nullable: false),
                    SlicesCompleted = table.Column<int>(type: "integer", nullable: false),
                    SourceBytes = table.Column<long>(type: "bigint", nullable: true),
                    DatabasesLoaded = table.Column<int>(type: "integer", nullable: false),
                    DumpBytes = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CutOverAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportJson = table.Column<string>(type: "text", nullable: true),
                    IssuedSecretsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationJobAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationJobAccounts_MigrationJobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "servermigration",
                        principalTable: "MigrationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobs_CreatedAt",
                schema: "servermigration",
                table: "MigrationJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobs_RemoteServerId",
                schema: "servermigration",
                table: "MigrationJobs",
                column: "RemoteServerId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobAccounts_JobId_SourceAccountName",
                schema: "servermigration",
                table: "MigrationJobAccounts",
                columns: new[] { "JobId", "SourceAccountName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobAccounts_JobId_State",
                schema: "servermigration",
                table: "MigrationJobAccounts",
                columns: new[] { "JobId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationJobAccounts",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "MigrationJobs",
                schema: "servermigration");
        }
    }
}
