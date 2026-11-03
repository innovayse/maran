using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResellers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResellerId",
                schema: "accounts",
                table: "Plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResellerId",
                schema: "accounts",
                table: "Accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Resellers",
                schema: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "en"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MaxAccounts = table.Column<int>(type: "integer", nullable: false),
                    DiskPoolMb = table.Column<long>(type: "bigint", nullable: false),
                    MaxPlans = table.Column<int>(type: "integer", nullable: false),
                    MaxDiskQuotaMb = table.Column<int>(type: "integer", nullable: false),
                    MaxSites = table.Column<int>(type: "integer", nullable: false),
                    MaxDatabases = table.Column<int>(type: "integer", nullable: false),
                    MaxSftpUsers = table.Column<int>(type: "integer", nullable: false),
                    MaxFtpUsers = table.Column<int>(type: "integer", nullable: false),
                    MaxCronEntries = table.Column<int>(type: "integer", nullable: false),
                    MaxPhpWorkersPerPool = table.Column<int>(type: "integer", nullable: false),
                    Features = table.Column<long>(type: "bigint", nullable: false),
                    MaxCpuQuotaPercent = table.Column<int>(type: "integer", nullable: true),
                    MaxMemoryMb = table.Column<int>(type: "integer", nullable: true),
                    MaxProcesses = table.Column<int>(type: "integer", nullable: true),
                    AllocationRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resellers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Plans_Name_Unowned",
                schema: "accounts",
                table: "Plans",
                column: "Name",
                unique: true,
                filter: "\"ResellerId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Plans_ResellerId_Name",
                schema: "accounts",
                table: "Plans",
                columns: new[] { "ResellerId", "Name" },
                unique: true,
                filter: "\"ResellerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ResellerId",
                schema: "accounts",
                table: "Accounts",
                column: "ResellerId");

            migrationBuilder.CreateIndex(
                name: "IX_Resellers_Name",
                schema: "accounts",
                table: "Resellers",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Resellers_ResellerId",
                schema: "accounts",
                table: "Accounts",
                column: "ResellerId",
                principalSchema: "accounts",
                principalTable: "Resellers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Plans_Resellers_ResellerId",
                schema: "accounts",
                table: "Plans",
                column: "ResellerId",
                principalSchema: "accounts",
                principalTable: "Resellers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Resellers_ResellerId",
                schema: "accounts",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Plans_Resellers_ResellerId",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropTable(
                name: "Resellers",
                schema: "accounts");

            migrationBuilder.DropIndex(
                name: "UX_Plans_Name_Unowned",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "UX_Plans_ResellerId_Name",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_ResellerId",
                schema: "accounts",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ResellerId",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ResellerId",
                schema: "accounts",
                table: "Accounts");
        }
    }
}
