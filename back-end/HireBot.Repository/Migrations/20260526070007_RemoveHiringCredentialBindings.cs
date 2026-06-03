using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemoveHiringCredentialBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringCredentialBindings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringCredentialBindings",
                columns: table => new
                {
                    BindingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    BindingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CredentialSlot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    HandoffId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "text", nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetSystem = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringCredentialBindings", x => x.BindingId);
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
        }
    }
}
