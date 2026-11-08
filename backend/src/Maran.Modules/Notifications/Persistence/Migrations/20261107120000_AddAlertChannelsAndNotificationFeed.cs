using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertChannelsAndNotificationFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertChannelStates",
                schema: "notifications",
                columns: table => new
                {
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertChannelStates", x => x.Channel);
                });

            migrationBuilder.CreateTable(
                name: "AlertRecipients",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRecipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PanelNotifications",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Arguments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DeepLink = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDismissals",
                schema: "notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDismissals", x => new { x.NotificationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_NotificationDismissals_PanelNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalSchema: "notifications",
                        principalTable: "PanelNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRecipients_Address",
                schema: "notifications",
                table: "AlertRecipients",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDismissals_UserId",
                schema: "notifications",
                table: "NotificationDismissals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelNotifications_OccurredAt",
                schema: "notifications",
                table: "PanelNotifications",
                column: "OccurredAt");

            // raw-sql: carries the one alert address the previous release kept in the mail settings into the
            // new recipients' list as its first entry; no parameters, no caller input. The address is lower-cased
            // because the list stores addresses that way. A panel with no mail settings, or a blank address,
            // gets no row. The old columns stay and are kept in step by the application for one release.
            migrationBuilder.Sql("""
                INSERT INTO notifications."AlertRecipients" ("Id", "Address", "Position", "AddedAt")
                SELECT gen_random_uuid(), lower(btrim("AlertRecipientAddress")), 0, "UpdatedAt"
                FROM notifications."SmtpSettings"
                WHERE btrim(COALESCE("AlertRecipientAddress", '')) <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertChannelStates",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "AlertRecipients",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "NotificationDismissals",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "PanelNotifications",
                schema: "notifications");
        }
    }
}
