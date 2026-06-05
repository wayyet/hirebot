using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HiringArtifacts 表增加 PackageId 列：最终包每次导入生成唯一值，中间包为 null
            migrationBuilder.AddColumn<string>(
                name: "PackageId",
                table: "HiringArtifacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_PackageId",
                table: "HiringArtifacts",
                column: "PackageId");

            // Instances 表增加 FinalPackageId 列：记录当前活跃的候选包版本 ID
            migrationBuilder.AddColumn<string>(
                name: "FinalPackageId",
                table: "Instances",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_FinalPackageId",
                table: "Instances",
                column: "FinalPackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Instances_FinalPackageId",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "FinalPackageId",
                table: "Instances");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifacts_PackageId",
                table: "HiringArtifacts");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "HiringArtifacts");
        }
    }
}
