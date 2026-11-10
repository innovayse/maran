using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Marketplace.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMarketplaceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketplace");

            migrationBuilder.CreateTable(
                name: "InstalledPackages",
                schema: "marketplace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InstalledBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StateChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceSettings",
                schema: "marketplace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModuleOperationLocks",
                schema: "marketplace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleOperationLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModuleVersionFloors",
                schema: "marketplace",
                columns: table => new
                {
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HighestVersionSeen = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleVersionFloors", x => x.ModuleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstalledPackages_ModuleId_InstalledAt",
                schema: "marketplace",
                table: "InstalledPackages",
                columns: new[] { "ModuleId", "InstalledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleOperationLocks_ModuleId",
                schema: "marketplace",
                table: "ModuleOperationLocks",
                column: "ModuleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstalledPackages",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "MarketplaceSettings",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "ModuleOperationLocks",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "ModuleVersionFloors",
                schema: "marketplace");
        }
    }
}
