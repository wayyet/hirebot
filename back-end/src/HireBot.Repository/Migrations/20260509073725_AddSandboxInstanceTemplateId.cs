using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxInstanceTemplateId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "SandboxInstances",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_TemplateId_SandboxRole_State",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "TemplateId", "SandboxRole", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SandboxInstances_OwnerSubject_TemplateId_SandboxRole_State",
                table: "SandboxInstances");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "SandboxInstances");
        }
    }
}
