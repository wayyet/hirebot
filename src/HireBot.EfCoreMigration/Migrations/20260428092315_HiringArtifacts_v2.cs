using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class HiringArtifacts_v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HiringSessions_TemplateId_PackageId_PackageVersion_PackageH~",
                table: "HiringSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HiringSessions_TemplateId_PackageId_PackageVersion_PackageH~",
                table: "HiringSessions",
                columns: new[] { "TemplateId", "PackageId", "PackageVersion", "PackageHash" });
        }
    }
}

