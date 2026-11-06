using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Idempotency.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdempotencySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "idempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallerKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReservationToken = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CallerKey_Key",
                schema: "idempotency",
                table: "IdempotencyRecords",
                columns: new[] { "CallerKey", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                schema: "idempotency",
                table: "IdempotencyRecords",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "idempotency");
        }
    }
}
