using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class FixHiringRuntimeStatesColumnNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HiringRuntimeStates 表在之前的迁移中被遗漏了，现在补充列重命名
            migrationBuilder.RenameColumn(
                name: "hire_id",
                table: "HiringRuntimeStates",
                newName: "HireId");

            migrationBuilder.RenameColumn(
                name: "session_id",
                table: "HiringRuntimeStates",
                newName: "SessionId");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "HiringRuntimeStates",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "current_stage",
                table: "HiringRuntimeStates",
                newName: "CurrentStage");

            migrationBuilder.RenameColumn(
                name: "collection_phase",
                table: "HiringRuntimeStates",
                newName: "CollectionPhase");

            migrationBuilder.RenameColumn(
                name: "payload_json",
                table: "HiringRuntimeStates",
                newName: "PayloadJson");

            migrationBuilder.RenameColumn(
                name: "packages_json",
                table: "HiringRuntimeStates",
                newName: "PackagesJson");

            migrationBuilder.RenameColumn(
                name: "workflow_state_json",
                table: "HiringRuntimeStates",
                newName: "WorkflowStateJson");

            migrationBuilder.RenameColumn(
                name: "conversation_cache_json",
                table: "HiringRuntimeStates",
                newName: "ConversationCacheJson");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "HiringRuntimeStates",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "HiringRuntimeStates",
                newName: "UpdatedAtUtc");

            // 重命名索引
            migrationBuilder.RenameIndex(
                name: "IX_HiringRuntimeStates_tenant_id_session_id",
                table: "HiringRuntimeStates",
                newName: "IX_HiringRuntimeStates_TenantId_SessionId");

            migrationBuilder.RenameIndex(
                name: "IX_HiringRuntimeStates_tenant_id_updated_at_utc",
                table: "HiringRuntimeStates",
                newName: "IX_HiringRuntimeStates_TenantId_UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚到 snake_case 命名
            migrationBuilder.RenameColumn(
                name: "HireId",
                table: "HiringRuntimeStates",
                newName: "hire_id");

            migrationBuilder.RenameColumn(
                name: "SessionId",
                table: "HiringRuntimeStates",
                newName: "session_id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "HiringRuntimeStates",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "CurrentStage",
                table: "HiringRuntimeStates",
                newName: "current_stage");

            migrationBuilder.RenameColumn(
                name: "CollectionPhase",
                table: "HiringRuntimeStates",
                newName: "collection_phase");

            migrationBuilder.RenameColumn(
                name: "PayloadJson",
                table: "HiringRuntimeStates",
                newName: "payload_json");

            migrationBuilder.RenameColumn(
                name: "PackagesJson",
                table: "HiringRuntimeStates",
                newName: "packages_json");

            migrationBuilder.RenameColumn(
                name: "WorkflowStateJson",
                table: "HiringRuntimeStates",
                newName: "workflow_state_json");

            migrationBuilder.RenameColumn(
                name: "ConversationCacheJson",
                table: "HiringRuntimeStates",
                newName: "conversation_cache_json");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "HiringRuntimeStates",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "HiringRuntimeStates",
                newName: "updated_at_utc");

            // 回滚索引命名
            migrationBuilder.RenameIndex(
                name: "IX_HiringRuntimeStates_TenantId_SessionId",
                table: "HiringRuntimeStates",
                newName: "IX_HiringRuntimeStates_tenant_id_session_id");

            migrationBuilder.RenameIndex(
                name: "IX_HiringRuntimeStates_TenantId_UpdatedAtUtc",
                table: "HiringRuntimeStates",
                newName: "IX_HiringRuntimeStates_tenant_id_updated_at_utc");
        }
    }
}
