using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.ServerMigration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigrationSchema : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "servermigration");

            migrationBuilder.CreateTable(
                name: "RemoteServers",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    SshUser = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowedFrom = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PrivateKeyCipher = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    HostKeyAlgorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HostKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    HostKeyFingerprint = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HostKeyConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastTestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SystemRelease = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MaranVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsRoot = table.Column<bool>(type: "boolean", nullable: true),
                    HomeFreeBytes = table.Column<long>(type: "bigint", nullable: true),
                    HomeTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    DatabaseServer = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    WebServer = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MailFound = table.Column<bool>(type: "boolean", nullable: true),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccountsFound = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemoteServerHostKeyCandidates",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Key = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteServerHostKeyCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteServerHostKeyCandidates_RemoteServers_RemoteServerId",
                        column: x => x.RemoteServerId,
                        principalSchema: "servermigration",
                        principalTable: "RemoteServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveredAccounts",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Home = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    DiskBytes = table.Column<long>(type: "bigint", nullable: false),
                    DiskKnown = table.Column<bool>(type: "boolean", nullable: false),
                    SiteCount = table.Column<int>(type: "integer", nullable: false),
                    DatabaseCount = table.Column<int>(type: "integer", nullable: false),
                    CronEntryCount = table.Column<int>(type: "integer", nullable: false),
                    MailboxCount = table.Column<int>(type: "integer", nullable: false),
                    DomainsText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredAccounts_RemoteServers_RemoteServerId",
                        column: x => x.RemoteServerId,
                        principalSchema: "servermigration",
                        principalTable: "RemoteServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveredSites",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscoveredAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    AliasesText = table.Column<string>(type: "text", nullable: false),
                    Backend = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhpVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DocumentRoot = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredSites_DiscoveredAccounts_DiscoveredAccountId",
                        column: x => x.DiscoveredAccountId,
                        principalSchema: "servermigration",
                        principalTable: "DiscoveredAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveredDatabases",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscoveredAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SizeKnown = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredDatabases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredDatabases_DiscoveredAccounts_DiscoveredAccountId",
                        column: x => x.DiscoveredAccountId,
                        principalSchema: "servermigration",
                        principalTable: "DiscoveredAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryWarnings",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryWarnings_RemoteServers_RemoteServerId",
                        column: x => x.RemoteServerId,
                        principalSchema: "servermigration",
                        principalTable: "RemoteServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MigrationSigningKeys",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateKey = table.Column<string>(type: "text", nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationSigningKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountExports",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BackupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DatabaseCount = table.Column<int>(type: "integer", nullable: false),
                    SectionsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountExports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExportGrants",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExportId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AllowedFrom = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ManifestReads = table.Column<int>(type: "integer", nullable: false),
                    ArtifactConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExportGrants_AccountExports_ExportId",
                        column: x => x.ExportId,
                        principalSchema: "servermigration",
                        principalTable: "AccountExports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountImports",
                schema: "servermigration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceAccountName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    SourcePrimaryDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    SourceHostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    KeyFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SectionsJson = table.Column<string>(type: "text", nullable: false),
                    ArtifactBackupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DatabaseCount = table.Column<int>(type: "integer", nullable: false),
                    HomeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TargetAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetResellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IssuedSecretsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountImports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteServers_Endpoint",
                schema: "servermigration",
                table: "RemoteServers",
                columns: new[] { "Host", "Port", "SshUser" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteServers_Name",
                schema: "servermigration",
                table: "RemoteServers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteServerHostKeyCandidates_RemoteServerId_Fingerprint",
                schema: "servermigration",
                table: "RemoteServerHostKeyCandidates",
                columns: new[] { "RemoteServerId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredAccounts_RemoteServerId_Name",
                schema: "servermigration",
                table: "DiscoveredAccounts",
                columns: new[] { "RemoteServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredSites_DiscoveredAccountId",
                schema: "servermigration",
                table: "DiscoveredSites",
                column: "DiscoveredAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredDatabases_DiscoveredAccountId",
                schema: "servermigration",
                table: "DiscoveredDatabases",
                column: "DiscoveredAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryWarnings_RemoteServerId",
                schema: "servermigration",
                table: "DiscoveryWarnings",
                column: "RemoteServerId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountExports_CreatedAt",
                schema: "servermigration",
                table: "AccountExports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AccountImports_CreatedAt",
                schema: "servermigration",
                table: "AccountImports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExportGrants_ExportId",
                schema: "servermigration",
                table: "ExportGrants",
                column: "ExportId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportGrants_TokenDigest",
                schema: "servermigration",
                table: "ExportGrants",
                column: "TokenDigest",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountImports",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "ExportGrants",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "AccountExports",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "MigrationSigningKeys",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "DiscoveryWarnings",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "DiscoveredDatabases",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "DiscoveredSites",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "DiscoveredAccounts",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "RemoteServerHostKeyCandidates",
                schema: "servermigration");

            migrationBuilder.DropTable(
                name: "RemoteServers",
                schema: "servermigration");
        }
    }
}
