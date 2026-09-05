using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Maran.Modules.Monitoring.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMonitoringSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "monitoring");

            migrationBuilder.CreateTable(
                name: "AlertStates",
                schema: "monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConsecutiveBreaches = table.Column<int>(type: "integer", nullable: false),
                    IsFiring = table.Column<bool>(type: "boolean", nullable: false),
                    RaisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Samples",
                schema: "monitoring",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    MemoryTotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    DiskUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    DiskTotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    NetworkRxBytes = table.Column<long>(type: "bigint", nullable: false),
                    NetworkTxBytes = table.Column<long>(type: "bigint", nullable: false),
                    LoadAverage1m = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Samples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertStates_Kind_Subject",
                schema: "monitoring",
                table: "AlertStates",
                columns: new[] { "Kind", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Samples_CapturedAt",
                schema: "monitoring",
                table: "Samples",
                column: "CapturedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertStates",
                schema: "monitoring");

            migrationBuilder.DropTable(
                name: "Samples",
                schema: "monitoring");
        }
    }
}
