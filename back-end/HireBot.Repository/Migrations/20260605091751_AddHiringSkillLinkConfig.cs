using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringSkillLinkConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringSkillLinkConfigs",
                columns: table => new
                {
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ConfigJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringSkillLinkConfigs", x => x.HireId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HiringSkillLinkConfigs_TenantId_UpdatedAtUtc",
                table: "HiringSkillLinkConfigs",
                columns: new[] { "TenantId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringSkillLinkConfigs");
        }
    }
}
