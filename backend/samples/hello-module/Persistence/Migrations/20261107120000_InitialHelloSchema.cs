using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Samples.Hello.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHelloSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hello");

            migrationBuilder.CreateTable(
                name: "Greetings",
                schema: "hello",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Greetings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Greetings_CreatedAt",
                schema: "hello",
                table: "Greetings",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Greetings",
                schema: "hello");
        }
    }
}
