using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMultitenancySupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SandboxSessions_OwnerSubject_ScopeType_ScopeKey_SandboxRole~",
                table: "SandboxSessions");

            migrationBuilder.DropIndex(
                name: "IX_HiringRuntimeStates_SessionId",
                table: "HiringRuntimeStates");

            migrationBuilder.DropIndex(
                name: "IX_HiringRuntimeStates_UpdatedAtUtc",
                table: "HiringRuntimeStates");

            migrationBuilder.DropIndex(
                name: "IX_HiringAuditLogs_HireId_TimestampUtc",
                table: "HiringAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_HiringAuditLogs_SessionId_TimestampUtc",
                table: "HiringAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifactUploads_SessionId_Kind_LogicalPath_CompletedA~",
                table: "HiringArtifactUploads");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifacts_SessionId_IsFinal",
                table: "HiringArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifacts_SessionId_Kind_LogicalPath",
                table: "HiringArtifacts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EvaluationWorkspaceStates",
                table: "EvaluationWorkspaceStates");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationSessions_EvaluatorHireId_TargetHireId",
                table: "EvaluationSessions");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationSessions_OwnerSubject_EmployeeId_UpdatedAtUtc",
                table: "EvaluationSessions");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SandboxSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SandboxAssets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "Instances",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "HiringSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "HiringRuntimeStates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "HiringAuditLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "HiringArtifactUploads",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "HiringArtifactUploadParts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "HiringArtifacts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "EvaluationWorkspaceStates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EvaluationWorkspaceStates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EvaluationSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EvaluationReports",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EvaluationAssets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_EvaluationWorkspaceStates",
                table: "EvaluationWorkspaceStates",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FamilyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GivenName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_Sa~",
                table: "SandboxSessions",
                columns: new[] { "TenantId", "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_TenantId",
                table: "SandboxAssets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_TenantId_SessionId",
                table: "HiringRuntimeStates",
                columns: new[] { "TenantId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_TenantId_UpdatedAtUtc",
                table: "HiringRuntimeStates",
                columns: new[] { "TenantId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_TenantId_HireId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "TenantId", "HireId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_TenantId_SessionId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "TenantId", "SessionId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploads_TenantId_SessionId_Kind_LogicalPath_C~",
                table: "HiringArtifactUploads",
                columns: new[] { "TenantId", "SessionId", "Kind", "LogicalPath", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploadParts_TenantId",
                table: "HiringArtifactUploadParts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_TenantId_SessionId_IsFinal",
                table: "HiringArtifacts",
                columns: new[] { "TenantId", "SessionId", "IsFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_TenantId_SessionId_Kind_LogicalPath",
                table: "HiringArtifacts",
                columns: new[] { "TenantId", "SessionId", "Kind", "LogicalPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationWorkspaceStates_TenantId_OwnerSubject_EmployeeId",
                table: "EvaluationWorkspaceStates",
                columns: new[] { "TenantId", "OwnerSubject", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_TenantId_EvaluatorHireId_TargetHireId",
                table: "EvaluationSessions",
                columns: new[] { "TenantId", "EvaluatorHireId", "TargetHireId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_TenantId_OwnerSubject_EmployeeId_Updated~",
                table: "EvaluationSessions",
                columns: new[] { "TenantId", "OwnerSubject", "EmployeeId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_TenantId",
                table: "EvaluationReports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationAssets_TenantId",
                table: "EvaluationAssets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_ExternalUserId",
                table: "AppUsers",
                column: "ExternalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId",
                table: "AppUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_ExternalUserId",
                table: "AppUsers",
                columns: new[] { "TenantId", "ExternalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_Username",
                table: "AppUsers",
                columns: new[] { "TenantId", "Username" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_Sa~",
                table: "SandboxSessions");

            migrationBuilder.DropIndex(
                name: "IX_SandboxAssets_TenantId",
                table: "SandboxAssets");

            migrationBuilder.DropIndex(
                name: "IX_HiringRuntimeStates_TenantId_SessionId",
                table: "HiringRuntimeStates");

            migrationBuilder.DropIndex(
                name: "IX_HiringRuntimeStates_TenantId_UpdatedAtUtc",
                table: "HiringRuntimeStates");

            migrationBuilder.DropIndex(
                name: "IX_HiringAuditLogs_TenantId_HireId_TimestampUtc",
                table: "HiringAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_HiringAuditLogs_TenantId_SessionId_TimestampUtc",
                table: "HiringAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifactUploads_TenantId_SessionId_Kind_LogicalPath_C~",
                table: "HiringArtifactUploads");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifactUploadParts_TenantId",
                table: "HiringArtifactUploadParts");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifacts_TenantId_SessionId_IsFinal",
                table: "HiringArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_HiringArtifacts_TenantId_SessionId_Kind_LogicalPath",
                table: "HiringArtifacts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EvaluationWorkspaceStates",
                table: "EvaluationWorkspaceStates");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationWorkspaceStates_TenantId_OwnerSubject_EmployeeId",
                table: "EvaluationWorkspaceStates");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationSessions_TenantId_EvaluatorHireId_TargetHireId",
                table: "EvaluationSessions");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationSessions_TenantId_OwnerSubject_EmployeeId_Updated~",
                table: "EvaluationSessions");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationReports_TenantId",
                table: "EvaluationReports");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationAssets_TenantId",
                table: "EvaluationAssets");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SandboxSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SandboxAssets");

            migrationBuilder.DropColumn(
                name: "description",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HiringRuntimeStates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HiringAuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HiringArtifactUploads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HiringArtifactUploadParts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HiringArtifacts");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "EvaluationWorkspaceStates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvaluationWorkspaceStates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvaluationSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvaluationReports");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvaluationAssets");

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "HiringSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_EvaluationWorkspaceStates",
                table: "EvaluationWorkspaceStates",
                columns: new[] { "OwnerSubject", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_OwnerSubject_ScopeType_ScopeKey_SandboxRole~",
                table: "SandboxSessions",
                columns: new[] { "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_SessionId",
                table: "HiringRuntimeStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_UpdatedAtUtc",
                table: "HiringRuntimeStates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_HireId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "HireId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_SessionId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "SessionId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploads_SessionId_Kind_LogicalPath_CompletedA~",
                table: "HiringArtifactUploads",
                columns: new[] { "SessionId", "Kind", "LogicalPath", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_SessionId_IsFinal",
                table: "HiringArtifacts",
                columns: new[] { "SessionId", "IsFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_SessionId_Kind_LogicalPath",
                table: "HiringArtifacts",
                columns: new[] { "SessionId", "Kind", "LogicalPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_EvaluatorHireId_TargetHireId",
                table: "EvaluationSessions",
                columns: new[] { "EvaluatorHireId", "TargetHireId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_OwnerSubject_EmployeeId_UpdatedAtUtc",
                table: "EvaluationSessions",
                columns: new[] { "OwnerSubject", "EmployeeId", "UpdatedAtUtc" });
        }
    }
}
