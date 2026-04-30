using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeChatImConfig_v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "corp_id",
                table: "IM_CONFIG",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_id",
                table: "IM_CONFIG",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_secret",
                table: "IM_CONFIG",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "corp_id", table: "IM_CONFIG");
            migrationBuilder.DropColumn(name: "agent_id", table: "IM_CONFIG");
            migrationBuilder.DropColumn(name: "agent_secret", table: "IM_CONFIG");
        }
    }
}
