using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringConversationCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConversationCacheJson",
                table: "HiringRuntimeStates",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversationCacheJson",
                table: "HiringRuntimeStates");
        }
    }
}
