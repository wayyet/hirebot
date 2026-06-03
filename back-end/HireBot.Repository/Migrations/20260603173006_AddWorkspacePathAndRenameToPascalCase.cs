using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspacePathAndRenameToPascalCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_hiring_material_files",
                table: "hiring_material_files");

            migrationBuilder.RenameTable(
                name: "hiring_material_files",
                newName: "HiringMaterialFiles");

            migrationBuilder.RenameColumn(
                name: "role",
                table: "Messages",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "content",
                table: "Messages",
                newName: "Content");

            migrationBuilder.RenameColumn(
                name: "channel",
                table: "Messages",
                newName: "Channel");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Messages",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "metadata_json",
                table: "Messages",
                newName: "MetadataJson");

            migrationBuilder.RenameColumn(
                name: "instance_id",
                table: "Messages",
                newName: "InstanceId");

            migrationBuilder.RenameColumn(
                name: "external_user_id",
                table: "Messages",
                newName: "ExternalUserId");

            migrationBuilder.RenameColumn(
                name: "external_message_id",
                table: "Messages",
                newName: "ExternalMessageId");

            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "Messages",
                newName: "ErrorMessage");

            migrationBuilder.RenameColumn(
                name: "delivery_status",
                table: "Messages",
                newName: "DeliveryStatus");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Messages",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "conversation_id",
                table: "Messages",
                newName: "ConversationId");

            migrationBuilder.RenameColumn(
                name: "message_id",
                table: "Messages",
                newName: "MessageId");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_instance_id_created_at",
                table: "Messages",
                newName: "IX_Messages_InstanceId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_instance_id_channel_created_at",
                table: "Messages",
                newName: "IX_Messages_InstanceId_Channel_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_conversation_id_created_at",
                table: "Messages",
                newName: "IX_Messages_ConversationId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_channel_external_message_id",
                table: "Messages",
                newName: "IX_Messages_Channel_ExternalMessageId");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Instances",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Instances",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Instances",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Instances",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "runtime_snapshot_json",
                table: "Instances",
                newName: "RuntimeSnapshotJson");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "Instances",
                newName: "OwnerUserId");

            migrationBuilder.RenameColumn(
                name: "instance_type",
                table: "Instances",
                newName: "InstanceType");

            migrationBuilder.RenameColumn(
                name: "from_instance_id",
                table: "Instances",
                newName: "FromInstanceId");

            migrationBuilder.RenameColumn(
                name: "eval_report_id",
                table: "Instances",
                newName: "EvalReportId");

            migrationBuilder.RenameColumn(
                name: "describe_document",
                table: "Instances",
                newName: "DescribeDocument");

            migrationBuilder.RenameColumn(
                name: "department_id",
                table: "Instances",
                newName: "DepartmentId");

            migrationBuilder.RenameColumn(
                name: "current_version",
                table: "Instances",
                newName: "CurrentVersion");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Instances",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "based_on_template_id",
                table: "Instances",
                newName: "BasedOnTemplateId");

            migrationBuilder.RenameColumn(
                name: "active_branch_id",
                table: "Instances",
                newName: "ActiveBranchId");

            migrationBuilder.RenameColumn(
                name: "instance_id",
                table: "Instances",
                newName: "InstanceId");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_tenant_id_department_id_instance_type_status",
                table: "Instances",
                newName: "IX_Instances_TenantId_DepartmentId_InstanceType_Status");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_owner_user_id_instance_type_status",
                table: "Instances",
                newName: "IX_Instances_OwnerUserId_InstanceType_Status");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_from_instance_id",
                table: "Instances",
                newName: "IX_Instances_FromInstanceId");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_based_on_template_id",
                table: "Instances",
                newName: "IX_Instances_BasedOnTemplateId");

            migrationBuilder.RenameColumn(
                name: "channel",
                table: "Conversations",
                newName: "Channel");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Conversations",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Conversations",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "Conversations",
                newName: "OwnerUserId");

            migrationBuilder.RenameColumn(
                name: "instance_id",
                table: "Conversations",
                newName: "InstanceId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Conversations",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "conversation_id",
                table: "Conversations",
                newName: "ConversationId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_tenant_id_updated_at",
                table: "Conversations",
                newName: "IX_Conversations_TenantId_UpdatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_instance_id_owner_user_id_channel",
                table: "Conversations",
                newName: "IX_Conversations_InstanceId_OwnerUserId_Channel");

            // Add workspace_relative_path column before renaming
            migrationBuilder.AddColumn<string>(
                name: "workspace_relative_path",
                table: "HiringMaterialFiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "sha256",
                table: "HiringMaterialFiles",
                newName: "Sha256");

            migrationBuilder.RenameColumn(
                name: "format",
                table: "HiringMaterialFiles",
                newName: "Format");

            migrationBuilder.RenameColumn(
                name: "workspace_relative_path",
                table: "HiringMaterialFiles",
                newName: "WorkspaceRelativePath");

            migrationBuilder.RenameColumn(
                name: "uploaded_by",
                table: "HiringMaterialFiles",
                newName: "UploadedBy");

            migrationBuilder.RenameColumn(
                name: "uploaded_at_utc",
                table: "HiringMaterialFiles",
                newName: "UploadedAtUtc");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "HiringMaterialFiles",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "HiringMaterialFiles",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "storage_path",
                table: "HiringMaterialFiles",
                newName: "StoragePath");

            migrationBuilder.RenameColumn(
                name: "size_bytes",
                table: "HiringMaterialFiles",
                newName: "SizeBytes");

            migrationBuilder.RenameColumn(
                name: "session_id",
                table: "HiringMaterialFiles",
                newName: "SessionId");

            migrationBuilder.RenameColumn(
                name: "requested_category_title",
                table: "HiringMaterialFiles",
                newName: "RequestedCategoryTitle");

            migrationBuilder.RenameColumn(
                name: "relative_path",
                table: "HiringMaterialFiles",
                newName: "RelativePath");

            migrationBuilder.RenameColumn(
                name: "original_file_name",
                table: "HiringMaterialFiles",
                newName: "OriginalFileName");

            migrationBuilder.RenameColumn(
                name: "operator_id",
                table: "HiringMaterialFiles",
                newName: "OperatorId");

            migrationBuilder.RenameColumn(
                name: "mime_type",
                table: "HiringMaterialFiles",
                newName: "MimeType");

            migrationBuilder.RenameColumn(
                name: "hire_id",
                table: "HiringMaterialFiles",
                newName: "HireId");

            migrationBuilder.RenameColumn(
                name: "deleted_at_utc",
                table: "HiringMaterialFiles",
                newName: "DeletedAtUtc");

            migrationBuilder.RenameColumn(
                name: "material_file_id",
                table: "HiringMaterialFiles",
                newName: "MaterialFileId");

            migrationBuilder.RenameIndex(
                name: "IX_hiring_material_files_sha256",
                table: "HiringMaterialFiles",
                newName: "IX_HiringMaterialFiles_Sha256");

            migrationBuilder.RenameIndex(
                name: "IX_hiring_material_files_session_id_relative_path",
                table: "HiringMaterialFiles",
                newName: "IX_HiringMaterialFiles_SessionId_RelativePath");

            migrationBuilder.RenameIndex(
                name: "IX_hiring_material_files_hire_id_session_id_uploaded_at_utc",
                table: "HiringMaterialFiles",
                newName: "IX_HiringMaterialFiles_HireId_SessionId_UploadedAtUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_HiringMaterialFiles",
                table: "HiringMaterialFiles",
                column: "MaterialFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_HiringMaterialFiles",
                table: "HiringMaterialFiles");

            migrationBuilder.RenameTable(
                name: "HiringMaterialFiles",
                newName: "hiring_material_files");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "Messages",
                newName: "role");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "Messages",
                newName: "content");

            migrationBuilder.RenameColumn(
                name: "Channel",
                table: "Messages",
                newName: "channel");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "Messages",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "Messages",
                newName: "metadata_json");

            migrationBuilder.RenameColumn(
                name: "InstanceId",
                table: "Messages",
                newName: "instance_id");

            migrationBuilder.RenameColumn(
                name: "ExternalUserId",
                table: "Messages",
                newName: "external_user_id");

            migrationBuilder.RenameColumn(
                name: "ExternalMessageId",
                table: "Messages",
                newName: "external_message_id");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "Messages",
                newName: "error_message");

            migrationBuilder.RenameColumn(
                name: "DeliveryStatus",
                table: "Messages",
                newName: "delivery_status");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Messages",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "Messages",
                newName: "conversation_id");

            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "Messages",
                newName: "message_id");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_InstanceId_CreatedAt",
                table: "Messages",
                newName: "IX_Messages_instance_id_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_InstanceId_Channel_CreatedAt",
                table: "Messages",
                newName: "IX_Messages_instance_id_channel_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_ConversationId_CreatedAt",
                table: "Messages",
                newName: "IX_Messages_conversation_id_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_Channel_ExternalMessageId",
                table: "Messages",
                newName: "IX_Messages_channel_external_message_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "Instances",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Instances",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Instances",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "Instances",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "RuntimeSnapshotJson",
                table: "Instances",
                newName: "runtime_snapshot_json");

            migrationBuilder.RenameColumn(
                name: "OwnerUserId",
                table: "Instances",
                newName: "owner_user_id");

            migrationBuilder.RenameColumn(
                name: "InstanceType",
                table: "Instances",
                newName: "instance_type");

            migrationBuilder.RenameColumn(
                name: "FromInstanceId",
                table: "Instances",
                newName: "from_instance_id");

            migrationBuilder.RenameColumn(
                name: "EvalReportId",
                table: "Instances",
                newName: "eval_report_id");

            migrationBuilder.RenameColumn(
                name: "DescribeDocument",
                table: "Instances",
                newName: "describe_document");

            migrationBuilder.RenameColumn(
                name: "DepartmentId",
                table: "Instances",
                newName: "department_id");

            migrationBuilder.RenameColumn(
                name: "CurrentVersion",
                table: "Instances",
                newName: "current_version");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Instances",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "BasedOnTemplateId",
                table: "Instances",
                newName: "based_on_template_id");

            migrationBuilder.RenameColumn(
                name: "ActiveBranchId",
                table: "Instances",
                newName: "active_branch_id");

            migrationBuilder.RenameColumn(
                name: "InstanceId",
                table: "Instances",
                newName: "instance_id");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_TenantId_DepartmentId_InstanceType_Status",
                table: "Instances",
                newName: "IX_Instances_tenant_id_department_id_instance_type_status");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_OwnerUserId_InstanceType_Status",
                table: "Instances",
                newName: "IX_Instances_owner_user_id_instance_type_status");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_FromInstanceId",
                table: "Instances",
                newName: "IX_Instances_from_instance_id");

            migrationBuilder.RenameIndex(
                name: "IX_Instances_BasedOnTemplateId",
                table: "Instances",
                newName: "IX_Instances_based_on_template_id");

            migrationBuilder.RenameColumn(
                name: "Channel",
                table: "Conversations",
                newName: "channel");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Conversations",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "Conversations",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "OwnerUserId",
                table: "Conversations",
                newName: "owner_user_id");

            migrationBuilder.RenameColumn(
                name: "InstanceId",
                table: "Conversations",
                newName: "instance_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Conversations",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "Conversations",
                newName: "conversation_id");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_TenantId_UpdatedAt",
                table: "Conversations",
                newName: "IX_Conversations_tenant_id_updated_at");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_InstanceId_OwnerUserId_Channel",
                table: "Conversations",
                newName: "IX_Conversations_instance_id_owner_user_id_channel");

            migrationBuilder.RenameColumn(
                name: "Sha256",
                table: "hiring_material_files",
                newName: "sha256");

            migrationBuilder.RenameColumn(
                name: "Format",
                table: "hiring_material_files",
                newName: "format");

            migrationBuilder.RenameColumn(
                name: "WorkspaceRelativePath",
                table: "hiring_material_files",
                newName: "workspace_relative_path");

            migrationBuilder.RenameColumn(
                name: "UploadedBy",
                table: "hiring_material_files",
                newName: "uploaded_by");

            migrationBuilder.RenameColumn(
                name: "UploadedAtUtc",
                table: "hiring_material_files",
                newName: "uploaded_at_utc");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "hiring_material_files",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "hiring_material_files",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StoragePath",
                table: "hiring_material_files",
                newName: "storage_path");

            migrationBuilder.RenameColumn(
                name: "SizeBytes",
                table: "hiring_material_files",
                newName: "size_bytes");

            migrationBuilder.RenameColumn(
                name: "SessionId",
                table: "hiring_material_files",
                newName: "session_id");

            migrationBuilder.RenameColumn(
                name: "RequestedCategoryTitle",
                table: "hiring_material_files",
                newName: "requested_category_title");

            migrationBuilder.RenameColumn(
                name: "RelativePath",
                table: "hiring_material_files",
                newName: "relative_path");

            migrationBuilder.RenameColumn(
                name: "OriginalFileName",
                table: "hiring_material_files",
                newName: "original_file_name");

            migrationBuilder.RenameColumn(
                name: "OperatorId",
                table: "hiring_material_files",
                newName: "operator_id");

            migrationBuilder.RenameColumn(
                name: "MimeType",
                table: "hiring_material_files",
                newName: "mime_type");

            migrationBuilder.RenameColumn(
                name: "HireId",
                table: "hiring_material_files",
                newName: "hire_id");

            migrationBuilder.RenameColumn(
                name: "DeletedAtUtc",
                table: "hiring_material_files",
                newName: "deleted_at_utc");

            migrationBuilder.RenameColumn(
                name: "MaterialFileId",
                table: "hiring_material_files",
                newName: "material_file_id");

            migrationBuilder.RenameIndex(
                name: "IX_HiringMaterialFiles_Sha256",
                table: "hiring_material_files",
                newName: "IX_hiring_material_files_sha256");

            migrationBuilder.RenameIndex(
                name: "IX_HiringMaterialFiles_SessionId_RelativePath",
                table: "hiring_material_files",
                newName: "IX_hiring_material_files_session_id_relative_path");

            migrationBuilder.RenameIndex(
                name: "IX_HiringMaterialFiles_HireId_SessionId_UploadedAtUtc",
                table: "hiring_material_files",
                newName: "IX_hiring_material_files_hire_id_session_id_uploaded_at_utc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_hiring_material_files",
                table: "hiring_material_files",
                column: "material_file_id");
        }
    }
}
