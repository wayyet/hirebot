using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class SplitHiringRuntimePayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 模板包定义：RoleTemplatePackage、WorkingTemplatePackage、DiscoverySkill。
            migrationBuilder.AddColumn<string>(
                name: "PackagesJson",
                table: "HiringRuntimeStates",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            // 动态工作流状态：StructuredData、Materials、HandoffItems、StageCompletion 等。
            migrationBuilder.AddColumn<string>(
                name: "WorkflowStateJson",
                table: "HiringRuntimeStates",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackagesJson",
                table: "HiringRuntimeStates");

            migrationBuilder.DropColumn(
                name: "WorkflowStateJson",
                table: "HiringRuntimeStates");
        }
    }
}
