using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class HiringWorkflowState_v1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringCredentialBindings",
                columns: table => new
                {
                    BindingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CredentialSlot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AuthKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TargetSystem = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TodoId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    BindingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringCredentialBindings", x => x.BindingId);
                });

            migrationBuilder.CreateTable(
                name: "HiringRuntimeStates",
                columns: table => new
                {
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CollectionPhase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringRuntimeStates", x => x.HireId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HiringCredentialBindings_HireId_UpdatedAtUtc",
                table: "HiringCredentialBindings",
                columns: new[] { "HireId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringCredentialBindings_SessionId_CredentialSlot",
                table: "HiringCredentialBindings",
                columns: new[] { "SessionId", "CredentialSlot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_SessionId",
                table: "HiringRuntimeStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_UpdatedAtUtc",
                table: "HiringRuntimeStates",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringCredentialBindings");

            migrationBuilder.DropTable(
                name: "HiringRuntimeStates");
        }
    }
}
