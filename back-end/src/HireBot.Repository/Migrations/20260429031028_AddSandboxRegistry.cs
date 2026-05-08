using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SandboxInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SandboxRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProvisioningMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    GatewayEndpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UseCase = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SandboxSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxInstanceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SandboxRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SessionKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SenderId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxSessions_SandboxInstances_SandboxInstanceEntityId",
                        column: x => x.SandboxInstanceEntityId,
                        principalTable: "SandboxInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SandboxAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxInstanceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SandboxSessionEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    MediaId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AssetRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxAssets_SandboxInstances_SandboxInstanceEntityId",
                        column: x => x.SandboxInstanceEntityId,
                        principalTable: "SandboxInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SandboxAssets_SandboxSessions_SandboxSessionEntityId",
                        column: x => x.SandboxSessionEntityId,
                        principalTable: "SandboxSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_MediaId",
                table: "SandboxAssets",
                column: "MediaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_SandboxInstanceEntityId_CreatedAtUtc",
                table: "SandboxAssets",
                columns: new[] { "SandboxInstanceEntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_SandboxSessionEntityId",
                table: "SandboxAssets",
                column: "SandboxSessionEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_ScopeType_ScopeKey_SandboxRole",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_State_UpdatedAtUtc",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_SandboxId",
                table: "SandboxInstances",
                column: "SandboxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_OwnerSubject_ScopeType_ScopeKey_SandboxRole~",
                table: "SandboxSessions",
                columns: new[] { "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_SandboxInstanceEntityId",
                table: "SandboxSessions",
                column: "SandboxInstanceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_SessionId",
                table: "SandboxSessions",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SandboxAssets");

            migrationBuilder.DropTable(
                name: "SandboxSessions");

            migrationBuilder.DropTable(
                name: "SandboxInstances");
        }
    }
}
