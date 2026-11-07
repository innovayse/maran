using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditRetentionChainAndErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                schema: "identity",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ChainIndex",
                schema: "identity",
                table: "AuditEvents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "CommitmentSalt",
                schema: "identity",
                table: "AuditEvents",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ErasableCommitment",
                schema: "identity",
                table: "AuditEvents",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ErasedAt",
                schema: "identity",
                table: "AuditEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowHash",
                schema: "identity",
                table: "AuditEvents",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditChainAnchors",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurgedThroughIndex = table.Column<long>(type: "bigint", nullable: false),
                    AnchorHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditChainAnchors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditRetentionPolicies",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRetentionPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AccountId",
                schema: "identity",
                table: "AuditEvents",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ChainIndex",
                schema: "identity",
                table: "AuditEvents",
                column: "ChainIndex",
                unique: true,
                filter: "\"ChainIndex\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Unsealed",
                schema: "identity",
                table: "AuditEvents",
                column: "OccurredAt",
                filter: "\"ChainIndex\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditChainAnchors",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "AuditRetentionPolicies",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_AccountId",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_ChainIndex",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_Unsealed",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "AccountId",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ChainIndex",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "CommitmentSalt",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ErasableCommitment",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ErasedAt",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "RowHash",
                schema: "identity",
                table: "AuditEvents");
        }
    }
}
